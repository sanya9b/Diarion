using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class ModelDownloadServiceTests : IDisposable
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("модель, вдай що ти вагомий файл");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "diarion-model-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ModelDownloadService CreateService() =>
        new(new HttpClient(_handler), new TempPathProvider(_root));

    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static AiModelDescriptor Model(string? sha = null) => new()
    {
        Id = "test-model",
        Kind = AiModelKind.Embedding,
        DisplayName = "Test",
        RepoId = "acme/test-model",
        RevisionSha = "abc123",
        Files =
        [
            new AiModelFile("onnx/model_qint8.onnx", "model.onnx", Payload.Length, sha ?? Sha256Of(Payload)),
        ],
        RequiredRamMb = 128,
        MinTier = DeviceTier.Low,
        LicenseSpdx = "Apache-2.0",
    };

    /// <summary>The real catalogue entries are multi-file; half-arrived is a state they can be in.</summary>
    private static AiModelDescriptor TwoFileModel() => new()
    {
        Id = "test-model",
        Kind = AiModelKind.Embedding,
        DisplayName = "Test",
        RepoId = "acme/test-model",
        RevisionSha = "abc123",
        Files =
        [
            new AiModelFile("onnx/model_qint8.onnx", "model.onnx", Payload.Length, Sha256Of(Payload)),
            new AiModelFile("onnx/second.onnx", "second.onnx", Payload.Length, Sha256Of(Payload)),
        ],
        RequiredRamMb = 128,
        MinTier = DeviceTier.Low,
        LicenseSpdx = "Apache-2.0",
    };

    private string LocalPath(string name = "model.onnx") => Path.Combine(_root, "test-model", name);

    [Fact]
    public void BuildFileUrl_PinsTheCommit_SoTheBytesCannotChangeUnderAReleasedApp()
    {
        var model = Model();

        model.BuildFileUrl(model.Files[0])
            .Should().Be("https://huggingface.co/acme/test-model/resolve/abc123/onnx/model_qint8.onnx");
    }

    [Fact]
    public async Task Download_WritesTheFileAndReportsInstalled()
    {
        _handler.RespondWith(Payload);
        var service = CreateService();
        var model = Model();

        (await service.DownloadAsync(model)).Should().BeTrue();

        File.ReadAllBytes(LocalPath()).Should().Equal(Payload);
        service.GetState(model).Should().Be(ModelInstallState.Installed);
    }

    [Fact]
    public async Task Download_DigestMismatch_FailsAndLeavesNothingBehind()
    {
        // A file that failed verification must not survive: the cheap size check on the next launch
        // would happily call it installed.
        _handler.RespondWith(Payload);
        var service = CreateService();
        var model = Model(sha: Sha256Of(Encoding.UTF8.GetBytes("щось інше")));

        (await service.DownloadAsync(model)).Should().BeFalse();

        File.Exists(LocalPath()).Should().BeFalse();
        File.Exists(LocalPath("model.onnx.partial")).Should().BeFalse();
        service.GetState(model).Should().Be(ModelInstallState.NotInstalled);
    }

    [Fact]
    public async Task Download_ServerError_Fails()
    {
        _handler.RespondWith(Array.Empty<byte>(), HttpStatusCode.NotFound);

        (await CreateService().DownloadAsync(Model())).Should().BeFalse();
    }

    [Fact]
    public async Task Download_ResumesFromAPartialFile()
    {
        var half = Payload.Length / 2;
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        File.WriteAllBytes(LocalPath("model.onnx.partial"), Payload.Take(half).ToArray());

        _handler.RespondWithRange(Payload);
        var service = CreateService();

        (await service.DownloadAsync(Model())).Should().BeTrue();

        _handler.LastRangeFrom.Should().Be(half);
        _handler.BytesServed.Should().Be(Payload.Length - half);
        File.ReadAllBytes(LocalPath()).Should().Equal(Payload);
    }

    [Fact]
    public async Task Download_ServerIgnoresRange_RestartsInsteadOfSplicing()
    {
        // Appending a full 200 response onto a partial would produce a corrupt file whose digest
        // fails, so this is really about not wasting the user's bandwidth twice.
        var half = Payload.Length / 2;
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        File.WriteAllBytes(LocalPath("model.onnx.partial"), Payload.Take(half).ToArray());

        _handler.RespondWith(Payload);

        (await CreateService().DownloadAsync(Model())).Should().BeTrue();

        File.ReadAllBytes(LocalPath()).Should().Equal(Payload);
    }

    [Fact]
    public async Task Download_OversizedPartial_IsDiscarded()
    {
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        File.WriteAllBytes(LocalPath("model.onnx.partial"), Payload.Concat(Payload).ToArray());

        _handler.RespondWith(Payload);

        (await CreateService().DownloadAsync(Model())).Should().BeTrue();

        File.ReadAllBytes(LocalPath()).Should().Equal(Payload);
    }

    [Fact]
    public async Task Download_AlreadyComplete_DoesNotHitTheNetwork()
    {
        _handler.RespondWith(Payload);
        var service = CreateService();
        await service.DownloadAsync(Model());
        var callsAfterFirst = _handler.RequestCount;

        (await service.DownloadAsync(Model())).Should().BeTrue();

        _handler.RequestCount.Should().Be(callsAfterFirst);
    }

    [Fact]
    public async Task Download_ReportsProgressEndingAtTheFullSize()
    {
        _handler.RespondWith(Payload);
        var seen = new List<ModelDownloadProgress>();

        await CreateService().DownloadAsync(Model(), new Progress<ModelDownloadProgress>(seen.Add));

        // Progress<T> marshals asynchronously, so wait for the queue to drain before asserting.
        await Task.Delay(50);
        seen.Should().NotBeEmpty();
        seen.Last().BytesReceived.Should().Be(Payload.Length);
        seen.Last().Fraction.Should().Be(1d);
    }

    [Fact]
    public async Task Download_Cancelled_Throws()
    {
        _handler.RespondWith(Payload);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await CreateService().DownloadAsync(Model(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetState_PartialWithNothingFetchingIt_IsInterrupted()
    {
        // The bug the user hit: this used to answer Downloading, and the settings row then offered
        // a cancel with nothing to cancel and hid both other buttons. Only reinstalling escaped it.
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        await File.WriteAllBytesAsync(LocalPath("model.onnx.partial"), Payload.Take(3).ToArray());

        CreateService().GetState(Model()).Should().Be(ModelInstallState.Interrupted);
    }

    [Fact]
    public async Task GetState_WhileTheDownloadRuns_IsDownloading()
    {
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var running = service.StartAsync(Model());

        await _handler.FirstRequestReceived;
        service.GetState(Model()).Should().Be(ModelInstallState.Downloading);
        service.IsActive("test-model").Should().BeTrue();

        _handler.Release();
        await running;
    }

    [Fact]
    public async Task GetState_OneFileOfTwoArrived_IsInterrupted()
    {
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        await File.WriteAllBytesAsync(LocalPath(), Payload);

        CreateService().GetState(TwoFileModel()).Should().Be(ModelInstallState.Interrupted);
    }

    [Fact]
    public async Task StartAsync_SecondCaller_JoinsTheDownloadInsteadOfStartingASecondOne()
    {
        // Two settings pages, or one page reopened, or a double tap. All the same download.
        _handler.RespondSlowly(Payload);
        var service = CreateService();

        var first = service.StartAsync(Model());
        await _handler.FirstRequestReceived;
        var second = service.StartAsync(Model());

        _handler.Release();
        (await first).Should().BeTrue();
        (await second).Should().BeTrue();
        _handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_AfterTheDownloadEnds_ForgetsIt()
    {
        // Otherwise the register grows a permanent "downloading" entry for a model already on disk.
        _handler.RespondWith(Payload);
        var service = CreateService();

        await service.StartAsync(Model());

        service.IsActive("test-model").Should().BeFalse();
        service.ActiveProgress("test-model").Should().BeNull();
        service.GetState(Model()).Should().Be(ModelInstallState.Installed);
    }

    [Fact]
    public async Task StartAsync_AnnouncesProgressToTheWholeApp()
    {
        // The page that started the download may be gone; whoever is on screen still needs the bar.
        _handler.RespondWith(Payload);
        var service = CreateService();
        var seen = new List<ModelDownloadProgress>();
        service.ProgressChanged += (_, p) => seen.Add(p);

        await service.StartAsync(Model());

        seen.Should().NotBeEmpty();
        seen[^1].BytesReceived.Should().Be(Payload.Length);
    }

    [Fact]
    public async Task Cancel_StopsTheDownloadAndKeepsTheBytesForAResume()
    {
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var running = service.StartAsync(Model());
        await _handler.FirstRequestReceived;

        service.Cancel("test-model");
        _handler.Release();

        // False, not an exception: a cancel is an outcome, not a fault.
        (await running).Should().BeFalse();
        service.IsActive("test-model").Should().BeFalse();
        service.GetState(Model()).Should().NotBe(ModelInstallState.Downloading);
    }

    [Fact]
    public void Cancel_NothingRunning_IsHarmless()
    {
        var act = () => CreateService().Cancel("test-model");

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Download_SilentServer_FailsInsteadOfWaitingForever()
    {
        // The stuck progress bar. A socket that is open and delivering nothing looks exactly like a
        // slow connection until something puts a clock on it.
        _handler.RespondWithSilence();
        var service = new ModelDownloadService(
            new HttpClient(_handler),
            new TempPathProvider(_root),
            stallTimeout: TimeSpan.FromMilliseconds(150));

        (await service.StartAsync(Model())).Should().BeFalse();
        service.IsActive("test-model").Should().BeFalse();
    }

    [Fact]
    public async Task Download_SilentServer_ReportsFailureRatherThanCancellation()
    {
        // The distinction the UI needs: our clock ran out, so the user has to see an error rather
        // than the silence that a cancel they asked for deserves.
        _handler.RespondWithSilence();
        var service = new ModelDownloadService(
            new HttpClient(_handler),
            new TempPathProvider(_root),
            stallTimeout: TimeSpan.FromMilliseconds(150));

        var act = async () => await service.DownloadAsync(Model());

        (await act.Should().NotThrowAsync()).Which.Should().BeFalse();
    }

    [Fact]
    public async Task ProgressOnDisk_CountsWholeFilesAndPartialsTogether()
    {
        // What the resume is worth, and the answer to "will this cost me the whole gigabyte again".
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        await File.WriteAllBytesAsync(LocalPath(), Payload);
        await File.WriteAllBytesAsync(LocalPath("second.onnx.partial"), Payload.Take(4).ToArray());

        var progress = CreateService().ProgressOnDisk(TwoFileModel());

        progress.BytesReceived.Should().Be(Payload.Length + 4);
        progress.TotalBytes.Should().Be(Payload.Length * 2);
    }

    [Fact]
    public void ProgressOnDisk_NothingThere_IsZero()
    {
        CreateService().ProgressOnDisk(Model()).Fraction.Should().Be(0d);
    }

    [Fact]
    public async Task Delete_DuringADownload_StopsItAndWaitsForTheHandleToClose()
    {
        // The copy holds the partial open with FileShare.None. Cancelling without waiting throws on
        // Windows, and a writer outliving the delete recreates what the user asked to be rid of.
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var running = service.StartAsync(Model());
        await _handler.FirstRequestReceived;

        var act = async () => await service.DeleteAsync(Model());

        await act.Should().NotThrowAsync();
        (await running).Should().BeFalse();
        service.IsActive("test-model").Should().BeFalse();
        service.GetState(Model()).Should().Be(ModelInstallState.NotInstalled);
    }

    [Fact]
    public async Task GetState_WrongSize_IsCorrupt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        await File.WriteAllBytesAsync(LocalPath(), Payload.Take(3).ToArray());

        CreateService().GetState(Model()).Should().Be(ModelInstallState.Corrupt);
    }

    [Fact]
    public void GetState_NothingOnDisk_IsNotInstalled()
    {
        CreateService().GetState(Model()).Should().Be(ModelInstallState.NotInstalled);
    }

    [Fact]
    public async Task Delete_RemovesEverythingIncludingPartials()
    {
        _handler.RespondWith(Payload);
        var service = CreateService();
        await service.DownloadAsync(Model());
        await File.WriteAllBytesAsync(LocalPath("stray.partial"), Payload);

        await service.DeleteAsync(Model());

        service.GetState(Model()).Should().Be(ModelInstallState.NotInstalled);
        Directory.Exists(Path.Combine(_root, "test-model")).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_NothingInstalled_IsHarmless()
    {
        var act = async () => await CreateService().DeleteAsync(Model());

        await act.Should().NotThrowAsync();
    }

    private sealed class TempPathProvider(string root) : IAiModelPathProvider
    {
        public string GetModelDirectory(string modelId) => Path.Combine(root, modelId);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private byte[] _body = Array.Empty<byte>();
        private HttpStatusCode _status = HttpStatusCode.OK;
        private bool _honourRange;

        /// <summary>Null delivers the body at once. Otherwise the body waits for this to be set.</summary>
        private TaskCompletionSource? _gate;

        public int RequestCount { get; private set; }

        public long? LastRangeFrom { get; private set; }

        public long BytesServed { get; private set; }

        /// <summary>Completes once the service has actually asked for something.</summary>
        public Task FirstRequestReceived => _firstRequest.Task;

        public void RespondWith(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
            _honourRange = false;
            _gate = null;
        }

        public void RespondWithRange(byte[] body)
        {
            _body = body;
            _status = HttpStatusCode.OK;
            _honourRange = true;
            _gate = null;
        }

        /// <summary>Headers now, body only on <see cref="Release"/> — a download caught mid-flight.</summary>
        public void RespondSlowly(byte[] body)
        {
            _body = body;
            _status = HttpStatusCode.OK;
            _honourRange = false;
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>The same gate, never opened: a socket that is up and delivering nothing.</summary>
        public void RespondWithSilence() => RespondSlowly(Array.Empty<byte>());

        public void Release() => _gate?.TrySetResult();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            _firstRequest.TrySetResult();

            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            LastRangeFrom = from;

            byte[] payload;
            HttpStatusCode status;

            if (_honourRange && from is > 0)
            {
                payload = _body.Skip((int)from.Value).ToArray();
                status = HttpStatusCode.PartialContent;
            }
            else
            {
                payload = _body;
                status = _status;
            }

            BytesServed = payload.Length;

            HttpContent content = _gate is null
                ? new ByteArrayContent(payload)
                : new StreamContent(new GatedStream(payload, _gate.Task));

            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    /// <summary>A body that arrives only once its gate opens, and honours cancellation while it waits.</summary>
    private sealed class GatedStream(byte[] payload, Task gate) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => payload.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            var remaining = payload.Length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var take = Math.Min(remaining, buffer.Length);
            payload.AsMemory(_position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
