using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models.Ai;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The settings row for a model, and specifically what happens when the page it lives on is
/// rebuilt underneath a running download — which the profile page does on every appearance.
/// </summary>
public class AiSettingsViewModelTests
{
    private const string ModelId = AiModelCatalog.MiniLmEncoderId;

    private readonly FakeDownloads _downloads = new();

    private AiSettingsViewModel CreateViewModel()
    {
        var probe = new Mock<IDeviceCapabilityProbe>();
        probe.Setup(p => p.Probe())
            .Returns(new DeviceCapabilities(8192, 100L * 1024 * 1024 * 1024, 8, Is64Bit: true));

        var dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(d => d.InvokeOnMainThread(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        return new AiSettingsViewModel(
            _downloads,
            probe.Object,
            new Mock<IEmbeddingIndexService>().Object,
            new Mock<IVectorStore>().Object,
            new Mock<ITextEmbedder>().Object,
            new Mock<IDialogService>().Object,
            dispatcher.Object);
    }

    private static AiModelItemViewModel Row(AiSettingsViewModel viewModel) =>
        viewModel.Models.First(m => m.Descriptor.Id == ModelId);

    [Fact]
    public async Task Reload_MidDownload_LeavesARowThatCanStillCancel()
    {
        // The reported bug. The page is transient and rebuilds on every appearance, so the row that
        // started the download is gone by the time the user comes back to stop it. It used to hold
        // the CancellationTokenSource, and the replacement row's was null: the cancel button did
        // nothing, download and delete were hidden, and the state never moved again.
        var viewModel = CreateViewModel();
        viewModel.Load();

        var started = Row(viewModel).DownloadCommand.ExecuteAsync(null);
        viewModel.Load();

        var reopened = Row(viewModel);
        reopened.IsDownloading.Should().BeTrue();
        reopened.CancelCommand.Execute(null);

        _downloads.CancelCount.Should().Be(1);

        // Completes rather than hanging: the cancel reached the download the first row started.
        await started;
    }

    [Fact]
    public async Task Reload_StartsNoSecondDownload()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        var running = Row(viewModel).DownloadCommand.ExecuteAsync(null);

        viewModel.Load();
        viewModel.Load();

        // Two reloads attach; only the button press starts anything.
        _downloads.StartCount.Should().Be(1);

        _downloads.Finish(succeeded: true);
        await running;
    }

    [Fact]
    public void Reload_StopsFeedingProgressToRowsNobodyCanSee()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        var discarded = Row(viewModel);

        viewModel.Load();
        _downloads.RaiseProgress(new ModelDownloadProgress(ModelId, 50, 100));

        Row(viewModel).ProgressFraction.Should().Be(0.5);
        discarded.ProgressFraction.Should().Be(0d, because: "a disposed row has unsubscribed");
    }

    [Fact]
    public void Unload_LetsGoOfTheSingletonsWithoutStoppingTheDownload()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        var row = Row(viewModel);

        viewModel.Unload();
        _downloads.RaiseProgress(new ModelDownloadProgress(ModelId, 90, 100));

        viewModel.Models.Should().BeEmpty();
        row.ProgressFraction.Should().Be(0d, because: "the page is gone and its rows have unsubscribed");
    }

    [Fact]
    public void Interrupted_OffersAResumeAndADeleteInsteadOfADeadCancel()
    {
        _downloads.State = ModelInstallState.Interrupted;
        var viewModel = CreateViewModel();
        viewModel.Load();

        var row = Row(viewModel);

        row.IsDownloading.Should().BeFalse();
        row.CanDownload.Should().BeTrue();
        row.CanDelete.Should().BeTrue(because: "half a model still occupies the storage");
        row.DownloadActionText.Should().Be(AppResources.AiResumeAction)
            .And.NotBe(AppResources.AiDownloadAction);
    }

    [Fact]
    public void Interrupted_StartsTheBarWhereTheDiskLeftOff()
    {
        // Otherwise resuming looks like starting the gigabyte over, which is the thing the user is
        // afraid of when they press the button.
        _downloads.State = ModelInstallState.Interrupted;
        _downloads.BytesOnDisk = AiModelCatalog.MiniLmEncoder.TotalSizeBytes / 4;
        var viewModel = CreateViewModel();
        viewModel.Load();

        var row = Row(viewModel);

        row.ShowProgress.Should().BeTrue();
        row.ProgressFraction.Should().BeApproximately(0.25, 0.01);
    }

    [Fact]
    public async Task Download_ThatEndsWithoutTheFiles_SaysSo()
    {
        // A stalled socket now fails instead of hanging, and a failure the user cannot see is the
        // same dead progress bar by another route.
        var viewModel = CreateViewModel();
        viewModel.Load();
        var row = Row(viewModel);

        var running = row.DownloadCommand.ExecuteAsync(null);
        _downloads.Finish(succeeded: false);
        await running;

        row.HasError.Should().BeTrue();
        row.CanDownload.Should().BeTrue(because: "there has to be a way to try again");
    }

    [Fact]
    public async Task Download_StoppedByTheUser_IsNotAnError()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        var row = Row(viewModel);

        var running = row.DownloadCommand.ExecuteAsync(null);
        _downloads.State = ModelInstallState.Interrupted;
        row.CancelCommand.Execute(null);
        await running;

        row.HasError.Should().BeFalse();
        row.State.Should().Be(ModelInstallState.Interrupted);
    }

    [Fact]
    public async Task Download_ThatSucceeds_LeavesTheRowInstalled()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        var row = Row(viewModel);

        var running = row.DownloadCommand.ExecuteAsync(null);
        _downloads.State = ModelInstallState.Installed;
        _downloads.Finish(succeeded: true);
        await running;

        row.State.Should().Be(ModelInstallState.Installed);
        row.HasError.Should().BeFalse();
        row.CanDelete.Should().BeTrue();
    }

    [Fact]
    public async Task ARowThatOpensMidDownload_LearnsWhenItEnds()
    {
        // Nothing tells the second page that a download it never started has finished, unless it
        // attaches to the running task.
        var viewModel = CreateViewModel();
        viewModel.Load();
        _ = Row(viewModel).DownloadCommand.ExecuteAsync(null);

        var reopened = CreateViewModel();
        reopened.Load();
        Row(reopened).IsDownloading.Should().BeTrue();

        _downloads.State = ModelInstallState.Installed;
        _downloads.Finish(succeeded: true);

        await WaitFor(() => Row(reopened).State == ModelInstallState.Installed);
    }

    [Fact]
    public async Task Delete_MidDownload_StopsItAndClearsTheRow()
    {
        var viewModel = CreateViewModel();
        viewModel.Load();
        var row = Row(viewModel);
        var running = row.DownloadCommand.ExecuteAsync(null);

        await row.DeleteCommand.ExecuteAsync(null);
        await running;

        _downloads.DeleteCount.Should().Be(1);
        row.State.Should().Be(ModelInstallState.NotInstalled);
        row.ProgressFraction.Should().Be(0d);
        row.HasError.Should().BeFalse(because: "the user asked for this");
    }

    /// <summary>For the one assertion that depends on a continuation nobody handed us a task for.</summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        condition().Should().BeTrue(because: "the condition should have been met within five seconds");
    }

    /// <summary>
    /// A download service whose downloads finish exactly when the test says so. The real one is
    /// covered by <see cref="ModelDownloadServiceTests"/>; what matters here is the handover
    /// between it and a view that comes and goes.
    /// </summary>
    private sealed class FakeDownloads : IModelDownloadService
    {
        private readonly Dictionary<string, TaskCompletionSource<bool>> _running = new(StringComparer.Ordinal);

        public event EventHandler<ModelDownloadProgress>? ProgressChanged;

        /// <summary>What the disk would say if nothing were running.</summary>
        public ModelInstallState State { get; set; } = ModelInstallState.NotInstalled;

        public long BytesOnDisk { get; set; }

        public int StartCount { get; private set; }

        public int CancelCount { get; private set; }

        public int DeleteCount { get; private set; }

        public ModelInstallState GetState(AiModelDescriptor model) =>
            IsActive(model.Id) ? ModelInstallState.Downloading : State;

        public bool IsActive(string modelId) => _running.ContainsKey(modelId);

        public ModelDownloadProgress? ActiveProgress(string modelId) =>
            _running.ContainsKey(modelId) ? new ModelDownloadProgress(modelId, BytesOnDisk, 100) : null;

        public ModelDownloadProgress ProgressOnDisk(AiModelDescriptor model) =>
            new(model.Id, BytesOnDisk, model.TotalSizeBytes);

        public Task<bool> StartAsync(AiModelDescriptor model)
        {
            if (_running.TryGetValue(model.Id, out var existing))
            {
                return existing.Task;
            }

            StartCount++;
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _running[model.Id] = pending;
            return pending.Task;
        }

        public void Cancel(string modelId)
        {
            CancelCount++;
            Finish(modelId, succeeded: false);
        }

        public Task<bool> DownloadAsync(
            AiModelDescriptor model,
            IProgress<ModelDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The view model uses StartAsync.");

        public Task DeleteAsync(AiModelDescriptor model)
        {
            DeleteCount++;
            Finish(model.Id, succeeded: false);
            State = ModelInstallState.NotInstalled;
            BytesOnDisk = 0;
            return Task.CompletedTask;
        }

        public void Finish(bool succeeded) => Finish(ModelId, succeeded);

        public void RaiseProgress(ModelDownloadProgress progress) => ProgressChanged?.Invoke(this, progress);

        private void Finish(string modelId, bool succeeded)
        {
            if (_running.Remove(modelId, out var pending))
            {
                pending.TrySetResult(succeeded);
            }
        }
    }
}
