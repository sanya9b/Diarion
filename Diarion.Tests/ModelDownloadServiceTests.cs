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
using Diarion.Models;
using Diarion.Models.Ai;
using Diarion.Services;
using Diarion.Services.Ai;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class ModelDownloadServiceTests : IDisposable
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("модель, вдай що ти вагомий файл");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "diarion-model-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();
    private readonly FakeNetworkStatus _network = new();
    private readonly FakeProfileService _profiles = new();
    private readonly RecordingTransferHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ModelDownloadService CreateService() =>
        new(new HttpModelFileTransfer(new HttpClient(_handler)), new TempPathProvider(_root), _profiles, _network, _host);

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
    public async Task Download_CompletePartial_IsVerifiedRatherThanFetchedAgain()
    {
        // The ordinary ending on iOS: the system finishes the transfer whether or not the app is
        // alive, so the next launch finds every byte on disk and nothing but the digest outstanding.
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        File.WriteAllBytes(LocalPath("model.onnx.partial"), Payload);

        _handler.RespondWith(Payload);

        (await CreateService().DownloadAsync(Model())).Should().BeTrue();

        _handler.RequestCount.Should().Be(0);
        File.ReadAllBytes(LocalPath()).Should().Equal(Payload);
    }

    [Fact]
    public async Task Download_CompletePartialThatIsNotTheModel_IsRejectedRatherThanInstalled()
    {
        // Right length, wrong bytes. Skipping the wire must not mean skipping the check — this is
        // the file the size shortcut would otherwise wave through into the model directory.
        Directory.CreateDirectory(Path.Combine(_root, "test-model"));
        File.WriteAllBytes(LocalPath("model.onnx.partial"), Payload.Select(b => (byte)~b).ToArray());

        _handler.RespondWith(Payload);

        (await CreateService().DownloadAsync(Model())).Should().BeFalse();

        File.Exists(LocalPath()).Should().BeFalse();
        File.Exists(LocalPath("model.onnx.partial")).Should().BeFalse();
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
        (await first).Should().Be(ModelDownloadOutcome.Completed);
        (await second).Should().Be(ModelDownloadOutcome.Completed);
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

        // An outcome, not an exception: a cancel is not a fault.
        (await running).Should().Be(ModelDownloadOutcome.Cancelled);
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
            new HttpModelFileTransfer(new HttpClient(_handler), stallTimeout: TimeSpan.FromMilliseconds(150)),
            new TempPathProvider(_root),
            _profiles,
            _network,
            _host);

        (await service.StartAsync(Model())).Should().Be(ModelDownloadOutcome.Failed);
        service.IsActive("test-model").Should().BeFalse();
    }

    [Fact]
    public async Task Download_SilentServer_ReportsFailureRatherThanCancellation()
    {
        // The distinction the UI needs: our clock ran out, so the user has to see an error rather
        // than the silence that a cancel they asked for deserves.
        _handler.RespondWithSilence();
        var service = new ModelDownloadService(
            new HttpModelFileTransfer(new HttpClient(_handler), stallTimeout: TimeSpan.FromMilliseconds(150)),
            new TempPathProvider(_root),
            _profiles,
            _network,
            _host);

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
        (await running).Should().Be(ModelDownloadOutcome.Cancelled);
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

    [Fact]
    public async Task WifiOnly_OnMobileData_RefusesBeforeOpeningASocket()
    {
        // The whole point of the setting: not one byte of the allowance is spent finding out.
        _network.Current = NetworkStatus.Metered;
        _handler.RespondWith(Payload);
        var service = CreateService();

        (await service.StartAsync(Model())).Should().Be(ModelDownloadOutcome.BlockedByMobileData);

        _handler.RequestCount.Should().Be(0);
        service.GetState(Model()).Should().Be(ModelInstallState.NotInstalled);
    }

    [Fact]
    public async Task WifiOnly_WithTheUsersConsentForThisOneFile_Downloads()
    {
        // What the settings row passes after the user reads the size and says yes anyway. The
        // stored preference is untouched, so the next model asks again.
        _network.Current = NetworkStatus.Metered;
        _handler.RespondWith(Payload);

        (await CreateService().StartAsync(Model(), allowMobileData: true))
            .Should().Be(ModelDownloadOutcome.Completed);

        _profiles.Profile.IsWifiOnlyModelDownload.Should().BeTrue();
    }

    [Fact]
    public async Task WifiOnly_Unticked_DownloadsOverMobileDataWithoutAsking()
    {
        _profiles.Profile.IsWifiOnlyModelDownload = false;
        _network.Current = NetworkStatus.Metered;
        _handler.RespondWith(Payload);

        (await CreateService().StartAsync(Model())).Should().Be(ModelDownloadOutcome.Completed);
    }

    [Fact]
    public async Task WifiOnly_ConnectionThePlatformWillNotClassify_IsNotTreatedAsMobileData()
    {
        // Deliberate direction of failure. Connectivity is not always sure what it is looking at,
        // and refusing on a maybe would break downloads that would have worked.
        _network.Current = NetworkStatus.Unknown;
        _handler.RespondWith(Payload);

        (await CreateService().StartAsync(Model())).Should().Be(ModelDownloadOutcome.Completed);
    }

    [Fact]
    public async Task WifiOnly_WifiDropsMidDownload_StopsAndSaysWhy()
    {
        // The case that actually costs money: the phone leaves the house, hands the transfer to
        // the cellular radio, and says nothing. Stopping here takes the same path as a user cancel,
        // so the bytes survive for a resume — see Cancel_StopsTheDownloadAndKeepsTheBytes.
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var running = service.StartAsync(Model());
        await _handler.FirstRequestReceived;

        _network.Current = NetworkStatus.Metered;
        _handler.Release();

        (await running).Should().Be(ModelDownloadOutcome.BlockedByMobileData);
        service.GetState(Model()).Should().NotBe(ModelInstallState.Installed);
        service.IsActive("test-model").Should().BeFalse();
    }

    [Fact]
    public async Task WifiOnly_AConnectionChangeThatIsStillWifi_LetsTheDownloadFinish()
    {
        // Moving between access points raises the same event. Only mobile data stops anything.
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var running = service.StartAsync(Model());
        await _handler.FirstRequestReceived;

        _network.Current = NetworkStatus.Unmetered;
        _handler.Release();

        (await running).Should().Be(ModelDownloadOutcome.Completed);
    }

    [Fact]
    public async Task WifiOnly_NetworkChangingAfterTheDownloadEnded_IsHarmless()
    {
        // The watcher outliving its download would be reaching for a disposed token source.
        _handler.RespondWith(Payload);
        var service = CreateService();
        await service.StartAsync(Model());

        var act = () => _network.Current = NetworkStatus.Metered;

        act.Should().NotThrow();
    }

    [Fact]
    public async Task WouldUseMobileData_OnWifi_IsFalse()
    {
        (await CreateService().WouldUseMobileDataAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task WouldUseMobileData_OnMobileDataWithTheSettingOn_IsTrue()
    {
        _network.Current = NetworkStatus.Metered;

        (await CreateService().WouldUseMobileDataAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task WouldUseMobileData_OnMobileDataWithTheSettingOff_IsFalse()
    {
        // Nothing to ask about: the user already said any network will do.
        _profiles.Profile.IsWifiOnlyModelDownload = false;
        _network.Current = NetworkStatus.Metered;

        (await CreateService().WouldUseMobileDataAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task WifiOnly_ProfileThatCannotBeRead_ErrsTowardsNotSpendingTheAllowance()
    {
        _network.Current = NetworkStatus.Metered;
        var service = new ModelDownloadService(
            new HttpModelFileTransfer(new HttpClient(_handler)),
            new TempPathProvider(_root),
            new BrokenProfiles(),
            _network,
            _host);

        (await service.StartAsync(Model())).Should().Be(ModelDownloadOutcome.BlockedByMobileData);
    }

    [Fact]
    public async Task TransferHost_IsHeldWhileBytesMoveAndReleasedWhenTheyStop()
    {
        // The whole point of it: while this is held, Android does not suspend the process, so a
        // locked screen no longer ends the download.
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var running = service.StartAsync(Model());
        await _handler.FirstRequestReceived;

        _host.IsHeld.Should().BeTrue();

        _handler.Release();
        (await running).Should().Be(ModelDownloadOutcome.Completed);

        _host.Begun.Should().Be(1);
        _host.IsHeld.Should().BeFalse();

        // Fed the same figures the in-app bar gets: when the app is away, the platform's
        // notification is the only thing the user can see.
        _host.Reports.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TransferHost_IsReleasedWhenTheUserCancels()
    {
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var running = service.StartAsync(Model());
        await _handler.FirstRequestReceived;

        service.Cancel("test-model");
        _handler.Release();
        await running;

        _host.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task TransferHost_IsReleasedWhenTheDownloadFails()
    {
        // A foreground service left running over a download that died is a notification the user
        // cannot dismiss and a battery drain they cannot explain.
        _handler.RespondWith(Array.Empty<byte>(), HttpStatusCode.NotFound);

        (await CreateService().StartAsync(Model())).Should().Be(ModelDownloadOutcome.Failed);

        _host.Begun.Should().Be(1);
        _host.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task TransferHost_IsReleasedWhenMobileDataBlocksTheDownload()
    {
        // Refused before a socket opens, which is the one ending that never reaches the try block.
        _network.Current = NetworkStatus.Metered;

        (await CreateService().StartAsync(Model())).Should().Be(ModelDownloadOutcome.BlockedByMobileData);

        _host.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task TransferHost_ASecondStartForTheSameModel_RaisesItOnce()
    {
        // Two views, one download — and one notification. Two would be one too many to dismiss.
        _handler.RespondSlowly(Payload);
        var service = CreateService();
        var first = service.StartAsync(Model());
        await _handler.FirstRequestReceived;
        var second = service.StartAsync(Model());

        _handler.Release();
        await Task.WhenAll(first, second);

        _host.Begun.Should().Be(1);
        _host.Released.Should().Be(1);
    }

    [Fact]
    public async Task TransferHost_ThatThrows_DoesNotCostTheDownload()
    {
        // A platform that refuses to keep us alive is a reason to download in the foreground, not
        // a reason to fail before the first byte.
        _handler.RespondWith(Payload);
        var service = new ModelDownloadService(
            new HttpModelFileTransfer(new HttpClient(_handler)),
            new TempPathProvider(_root),
            _profiles,
            _network,
            new HostileTransferHost());

        (await service.StartAsync(Model())).Should().Be(ModelDownloadOutcome.Completed);
    }

    [Fact]
    public async Task Progress_Verifying_IsAnnouncedAndThenCleared()
    {
        // SHA-256 over a gigabyte is ten seconds of a bar that has stopped, which reads as a
        // freeze. The phase is the only thing that tells the two apart.
        _handler.RespondWith(Payload);
        var service = CreateService();
        var seen = new List<ModelDownloadProgress>();
        service.ProgressChanged += (_, p) => seen.Add(p);

        await service.StartAsync(Model());

        seen.Should().Contain(p => p.Phase == ModelDownloadPhase.Verifying);
        seen[^1].Phase.Should().Be(ModelDownloadPhase.Transferring);
    }

    [Fact]
    public async Task Progress_Verifying_CarriesTheByteCountRatherThanRewindingTheBar()
    {
        _handler.RespondWith(Payload);
        var service = CreateService();
        var seen = new List<ModelDownloadProgress>();
        service.ProgressChanged += (_, p) => seen.Add(p);

        await service.StartAsync(Model());

        seen.First(p => p.Phase == ModelDownloadPhase.Verifying)
            .BytesReceived.Should().Be(Payload.Length);
    }

    /// <summary>Counts what the platform was asked for, so the tests can prove it was let go of.</summary>
    private sealed class RecordingTransferHost : IModelTransferHost
    {
        private readonly object _gate = new();
        private readonly List<ModelDownloadProgress> _reports = new();

        public bool KeepsRunningInBackground => true;

        public int Begun { get; private set; }

        public int Released { get; private set; }

        public bool IsHeld
        {
            get
            {
                lock (_gate)
                {
                    return Begun > Released;
                }
            }
        }

        public IReadOnlyList<ModelDownloadProgress> Reports
        {
            get
            {
                lock (_gate)
                {
                    return _reports.ToList();
                }
            }
        }

        public IDisposable Begin(string modelId, string modelName)
        {
            lock (_gate)
            {
                Begun++;
            }

            return new Handle(this);
        }

        public void Report(ModelDownloadProgress progress)
        {
            lock (_gate)
            {
                _reports.Add(progress);
            }
        }

        private void Release()
        {
            lock (_gate)
            {
                Released++;
            }
        }

        private sealed class Handle(RecordingTransferHost owner) : IDisposable
        {
            private bool _released;

            public void Dispose()
            {
                if (_released)
                {
                    return;
                }

                _released = true;
                owner.Release();
            }
        }
    }

    /// <summary>A platform having a bad day: every call into it throws.</summary>
    private sealed class HostileTransferHost : IModelTransferHost
    {
        public bool KeepsRunningInBackground => true;

        public IDisposable Begin(string modelId, string modelName) =>
            throw new InvalidOperationException("the system refused to start the service");

        public void Report(ModelDownloadProgress progress) =>
            throw new InvalidOperationException("the notification is gone");
    }

    private sealed class BrokenProfiles : IProfileService
    {
        public Task<UserProfile> GetUserProfileAsync() => throw new InvalidOperationException("database is busy");

        public Task SaveUserProfileAsync(UserProfile profile) => Task.CompletedTask;

        public Task ClearAllDataAsync() => Task.CompletedTask;
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
