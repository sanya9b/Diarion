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
    public async Task GetState_PartialPresent_IsDownloading()
    {
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        await File.WriteAllBytesAsync(LocalPath("model.onnx.partial"), Payload.Take(3).ToArray());

        CreateService().GetState(Model()).Should().Be(ModelInstallState.Downloading);
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

        service.Delete(Model());

        service.GetState(Model()).Should().Be(ModelInstallState.NotInstalled);
        Directory.Exists(Path.Combine(_root, "test-model")).Should().BeFalse();
    }

    [Fact]
    public void Delete_NothingInstalled_IsHarmless()
    {
        var act = () => CreateService().Delete(Model());

        act.Should().NotThrow();
    }

    private sealed class TempPathProvider(string root) : IAiModelPathProvider
    {
        public string GetModelDirectory(string modelId) => Path.Combine(root, modelId);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private byte[] _body = Array.Empty<byte>();
        private HttpStatusCode _status = HttpStatusCode.OK;
        private bool _honourRange;

        public int RequestCount { get; private set; }

        public long? LastRangeFrom { get; private set; }

        public long BytesServed { get; private set; }

        public void RespondWith(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
            _honourRange = false;
        }

        public void RespondWithRange(byte[] body)
        {
            _body = body;
            _status = HttpStatusCode.OK;
            _honourRange = true;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;

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

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(payload),
            });
        }
    }
}
