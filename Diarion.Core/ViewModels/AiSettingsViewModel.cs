using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models.Ai;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Ai;

namespace Diarion.ViewModels;

/// <summary>
/// The AI tab of the settings screen: what this device can run, which models are installed, and
/// how current the search index is.
/// </summary>
public partial class AiSettingsViewModel : BaseViewModel
{
    private readonly IModelDownloadService _downloads;
    private readonly IDeviceCapabilityProbe _probe;
    private readonly IEmbeddingIndexService _index;
    private readonly IVectorStore _store;
    private readonly ITextEmbedder _embedder;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcher;

    [ObservableProperty] private string _deviceSummary = string.Empty;
    [ObservableProperty] private string _tierDescription = string.Empty;
    [ObservableProperty] private string _indexStatus = AppResources.AiIndexIdle;
    [ObservableProperty] private double _indexFraction;
    [ObservableProperty] private string _indexedChunks = string.Empty;
    [ObservableProperty] private bool _isIndexing;

    public ObservableCollection<AiModelItemViewModel> Models { get; } = [];

    public AiSettingsViewModel(
        IModelDownloadService downloads,
        IDeviceCapabilityProbe probe,
        IEmbeddingIndexService index,
        IVectorStore store,
        ITextEmbedder embedder,
        IDialogService dialogService,
        IDispatcherService dispatcher)
    {
        _downloads = downloads;
        _probe = probe;
        _index = index;
        _store = store;
        _embedder = embedder;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
    }

    public void Load()
    {
        // Symmetric with Unload, and idempotent: a page can appear twice with no disappearance in
        // between, and every subscription below is to a singleton that outlives this view model.
        Unload();
        _index.ProgressChanged += OnIndexProgressChanged;

        var capabilities = _probe.Probe();

        DeviceSummary = string.Format(
            AppResources.AiDeviceSummaryFormat,
            ByteSize.Describe((long)capabilities.TotalRamMb * 1024 * 1024),
            ByteSize.Describe(capabilities.AvailableStorageBytes));

        TierDescription = capabilities.Tier switch
        {
            DeviceTier.High => AppResources.AiTierHigh,
            DeviceTier.Mid => AppResources.AiTierMid,
            _ => AppResources.AiTierLow,
        };

        var recommended = new HashSet<string>(
            Enum.GetValues<AiModelKind>()
                .Select(kind => AiModelCatalog.Recommend(kind, capabilities)?.Id)
                .Where(id => id is not null)!);

        foreach (var model in AiModelCatalog.All)
        {
            Models.Add(new AiModelItemViewModel(
                model,
                _downloads,
                capabilities,
                recommended.Contains(model.Id),
                _dispatcher,
                _dialogService,
                OnModelChanged));
        }

        RefreshIndexStatus(_index.Progress);
    }

    /// <summary>
    /// Lets go of the singletons when the page goes away. A download that is running keeps running
    /// — the service owns it — and the next <see cref="Load"/> picks it back up where it is.
    /// </summary>
    public void Unload()
    {
        _index.ProgressChanged -= OnIndexProgressChanged;

        // Disposed, not just dropped: each row is a listener on the download service.
        foreach (var stale in Models)
        {
            stale.Dispose();
        }

        Models.Clear();
    }

    /// <summary>
    /// A model arriving or leaving changes what the runtime can load, so the index has to be told:
    /// a fresh download means there is work to do, and a deletion means its rows are unusable.
    /// </summary>
    private async void OnModelChanged(AiModelItemViewModel item)
    {
        if (item.Descriptor.Kind != AiModelKind.Embedding)
        {
            return;
        }

        if (item.State == ModelInstallState.Installed)
        {
            _index.Start();
        }
        else
        {
            await _index.ClearAsync();
        }

        RefreshIndexStatus(_index.Progress);
    }

    private void OnIndexProgressChanged(object? sender, AiIndexProgress progress) =>
        _dispatcher.InvokeOnMainThread(() => RefreshIndexStatus(progress));

    private void RefreshIndexStatus(AiIndexProgress progress)
    {
        IsIndexing = progress.Phase is AiIndexPhase.Scanning or AiIndexPhase.Embedding;
        IndexFraction = progress.Fraction;

        IndexStatus = progress.Phase switch
        {
            AiIndexPhase.Scanning => AppResources.AiIndexScanning,
            AiIndexPhase.Embedding => string.Format(AppResources.AiIndexEmbeddingFormat, progress.Done, progress.Total),
            AiIndexPhase.Complete => AppResources.AiIndexComplete,
            AiIndexPhase.Cancelled => AppResources.AiIndexCancelled,
            _ => AppResources.AiIndexIdle,
        };

        IndexedChunks = _embedder.IsAvailable
            ? string.Format(AppResources.AiIndexChunksFormat, _store.CountForModel(_embedder.ModelId))
            : string.Empty;
    }

    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        // Dropping first is what makes this a rebuild rather than a no-op: the queue is derived
        // from stored hashes, so an intact index has nothing to do.
        await _index.ClearAsync();
        _index.Start();
    }

    [RelayCommand]
    private async Task ClearIndexAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            AppResources.AiIndexClearAction,
            AppResources.AiIndexClearConfirm,
            AppResources.AiDeleteAction,
            AppResources.AiCancelAction);

        if (!confirmed)
        {
            return;
        }

        await _index.ClearAsync();
        RefreshIndexStatus(_index.Progress);
    }
}

/// <summary>One row in the model list: what it is, whether it is here, and what can be done to it.</summary>
/// <remarks>
/// Deliberately owns no download. This view model is rebuilt every time the profile page appears,
/// and a gigabyte takes longer than a user stays on one screen — so the row attaches to whatever
/// the download service is already doing rather than holding a task the next row cannot reach.
/// </remarks>
public partial class AiModelItemViewModel : ObservableObject, IDisposable
{
    private readonly IModelDownloadService _downloads;
    private readonly IDispatcherService _dispatcher;
    private readonly IDialogService _dialogService;
    private readonly Action<AiModelItemViewModel> _onChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    [NotifyPropertyChangedFor(nameof(HasProgressDetail))]
    [NotifyPropertyChangedFor(nameof(DownloadActionText))]
    private ModelInstallState _state;

    [ObservableProperty] private double _progressFraction;

    /// <summary>Bytes, rate and estimate, or empty while there is nothing yet to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgressDetail))]
    private string _progressDetail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public AiModelItemViewModel(
        AiModelDescriptor descriptor,
        IModelDownloadService downloads,
        DeviceCapabilities capabilities,
        bool isRecommended,
        IDispatcherService dispatcher,
        IDialogService dialogService,
        Action<AiModelItemViewModel> onChanged)
    {
        Descriptor = descriptor;
        _downloads = downloads;
        _dispatcher = dispatcher;
        _dialogService = dialogService;
        _onChanged = onChanged;

        IsRecommended = isRecommended;
        CanRunHere = descriptor.MinTier <= capabilities.Tier && descriptor.RequiredRamMb <= capabilities.TotalRamMb;
        SizeText = ByteSize.Describe(descriptor.TotalSizeBytes);
        State = downloads.GetState(descriptor);

        // Whatever is already there — an in-flight download's live figure, or the bytes an
        // interrupted one left behind. Starting at zero would tell the user the resume is a restart.
        var already = downloads.ActiveProgress(descriptor.Id) ?? downloads.ProgressOnDisk(descriptor);
        ProgressFraction = already.Fraction;
        ProgressDetail = ShowProgress ? ModelProgressText.Describe(already) : string.Empty;

        _downloads.ProgressChanged += OnProgressChanged;

        if (State == ModelInstallState.Downloading)
        {
            // A download this row did not start. Joining it is how the row learns it finished.
            _ = TrackAsync();
        }
    }

    private void OnProgressChanged(object? sender, ModelDownloadProgress progress)
    {
        if (!string.Equals(progress.ModelId, Descriptor.Id, StringComparison.Ordinal))
        {
            return;
        }

        // The service reports from whatever thread the socket read completed on.
        _dispatcher.InvokeOnMainThread(() =>
        {
            ProgressFraction = progress.Fraction;
            ProgressDetail = ModelProgressText.Describe(progress);
        });
    }

    public void Dispose() => _downloads.ProgressChanged -= OnProgressChanged;

    public AiModelDescriptor Descriptor { get; }

    public string DisplayName => Descriptor.DisplayName;

    public string SizeText { get; }

    public string LicenseText => Descriptor.LicenseSpdx;

    public bool IsRecommended { get; }

    /// <summary>False when the device is below the model's tier. The row stays visible and says why.</summary>
    public bool CanRunHere { get; }

    // Negations and emptiness checks are properties rather than XAML converters: this project has
    // no IValueConverter anywhere, and one binding is not a reason to start.
    public bool ShowUnavailableReason => !CanRunHere;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string UnavailableReason => CanRunHere ? string.Empty : AppResources.AiModelTooLargeForDevice;

    public bool IsDownloading => State == ModelInstallState.Downloading;

    /// <summary>
    /// The bar stays up for an interrupted download too — the bytes on disk are real, and hiding
    /// them would make resuming look like starting over.
    /// </summary>
    public bool ShowProgress => State is ModelInstallState.Downloading or ModelInstallState.Interrupted;

    public bool HasProgressDetail => ShowProgress && !string.IsNullOrEmpty(ProgressDetail);

    /// <summary>
    /// What the user is told about walking away. Read from the service rather than assumed,
    /// because "you can minimize this" is a promise only Android and iOS can keep, and on a
    /// desktop build it would be a lie that costs someone a gigabyte.
    /// </summary>
    public string BackgroundNotice => _downloads.KeepsRunningInBackground
        ? AppResources.AiBackgroundNotice
        : AppResources.AiKeepOpenNotice;

    public bool CanDownload => CanRunHere && State is
        ModelInstallState.NotInstalled or ModelInstallState.Corrupt or ModelInstallState.Interrupted;

    /// <summary>Half a model is still worth a delete button — it is how the user reclaims the space.</summary>
    public bool CanDelete => State is
        ModelInstallState.Installed or ModelInstallState.Corrupt or ModelInstallState.Interrupted;

    /// <summary>"Download" the first time, "Resume" when there is something to resume.</summary>
    public string DownloadActionText => State == ModelInstallState.Interrupted
        ? AppResources.AiResumeAction
        : AppResources.AiDownloadAction;

    public string StateText => State switch
    {
        ModelInstallState.Installed => AppResources.AiStateInstalled,
        ModelInstallState.Downloading => AppResources.AiStateDownloading,
        ModelInstallState.Corrupt => AppResources.AiStateCorrupt,
        ModelInstallState.Interrupted => AppResources.AiStateInterrupted,
        _ => AppResources.AiStateNotInstalled,
    };

    [RelayCommand]
    private async Task DownloadAsync()
    {
        ErrorMessage = string.Empty;

        var allowMobileData = false;
        if (await _downloads.WouldUseMobileDataAsync())
        {
            // Asked rather than refused. The preference is the user's own, and someone a week from
            // any Wi-Fi should be able to overrule it for one file without walking back to the
            // checkbox — and without the checkbox silently staying off afterwards.
            allowMobileData = await _dialogService.ShowConfirmationAsync(
                AppResources.AiMobileDataTitle,
                string.Format(AppResources.AiMobileDataConfirmFormat, SizeText),
                AppResources.AiMobileDataDownloadAnyway,
                AppResources.AiCancelAction);

            if (!allowMobileData)
            {
                // They just read why. Repeating it in the row would be telling them their own answer.
                return;
            }
        }

        State = ModelInstallState.Downloading;

        await TrackAsync(allowMobileData);
    }

    /// <summary>
    /// Follows a download to its end, whether or not this row started it. Only the outcome is
    /// handled here — the bytes arrive through the service's progress event.
    /// </summary>
    private async Task TrackAsync(bool allowMobileData = false)
    {
        // Joins the running download rather than starting a second one, and does not throw: every
        // ending, cancellation included, comes back as an outcome.
        var outcome = await _downloads.StartAsync(Descriptor, allowMobileData);

        State = _downloads.GetState(Descriptor);

        var onDisk = _downloads.ProgressOnDisk(Descriptor);
        ProgressFraction = onDisk.Fraction;

        // Bytes without a rate. What is on disk still answers "will resuming cost me the whole
        // gigabyte"; a speed left over from a download that stopped would read as one that has not.
        ProgressDetail = State == ModelInstallState.Interrupted
            ? ModelProgressText.Describe(onDisk)
            : string.Empty;

        // Stopping on request is not a failure. Anything else that ends without the files is, and
        // has to say so — a stalled network is otherwise indistinguishable from a slow one.
        ErrorMessage = outcome switch
        {
            ModelDownloadOutcome.Completed or ModelDownloadOutcome.Cancelled => string.Empty,
            ModelDownloadOutcome.BlockedByMobileData => AppResources.AiDownloadWifiOnlyStopped,
            _ => AppResources.AiDownloadFailed,
        };

        _onChanged(this);
    }

    [RelayCommand]
    private void Cancel() =>
        // Cancelling only asks; the running task notices and unwinds, and TrackAsync writes the
        // state that follows. Bytes stay on disk, so the next tap resumes.
        _downloads.Cancel(Descriptor.Id);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        // Awaits: deleting mid-download has to wait for the writer to let go of the file.
        await _downloads.DeleteAsync(Descriptor);

        ErrorMessage = string.Empty;
        ProgressFraction = 0;
        ProgressDetail = string.Empty;
        State = _downloads.GetState(Descriptor);
        _onChanged(this);
    }
}

/// <summary>Human-readable file sizes. Binary units, because that is what storage dialogs show.</summary>
public static class ByteSize
{
    public static string Describe(long bytes)
    {
        // Localized, because the settings screen puts this next to Ukrainian prose and "11.4 GB
        // вільно" reads as a bug even though it is only a unit.
        string[] units =
        [
            AppResources.UnitByte,
            AppResources.UnitKilobyte,
            AppResources.UnitMegabyte,
            AppResources.UnitGigabyte,
            AppResources.UnitTerabyte,
        ];

        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[0]}" : $"{value:0.#} {units[unit]}";
    }
}

/// <summary>
/// The one sentence a download gets to say about itself: <c>312 MB of 1.1 GB · 4.2 MB/s · ~3 min
/// left</c>.
/// </summary>
/// <remarks>
/// Shared, because the settings row and the Android notification say the same thing to the same
/// person about the same download, and two spellings of it would drift. It sits beside
/// <see cref="ByteSize"/> for the same reason that does: everything here is presentation, and the
/// download service has no business knowing how a megabyte is spelled in Ukrainian.
/// </remarks>
public static class ModelProgressText
{
    /// <summary>
    /// Below this an estimate is arithmetic, not information — a rate measured over a few
    /// megabytes says nothing about the hour that follows.
    /// </summary>
    private static readonly TimeSpan ShortestWorthShowing = TimeSpan.FromSeconds(5);

    public static string Describe(ModelDownloadProgress progress)
    {
        if (progress.Phase == ModelDownloadPhase.Verifying)
        {
            // No bytes are moving and none are left to count. Saying so is the whole point: the
            // bar sits still for ten seconds here, and silence reads as a freeze.
            return AppResources.AiStateVerifying;
        }

        var parts = new List<string>(3)
        {
            string.Format(
                AppResources.AiDownloadOfFormat,
                ByteSize.Describe(progress.BytesReceived),
                ByteSize.Describe(progress.TotalBytes)),
        };

        if (progress.BytesPerSecond > 0)
        {
            parts.Add(string.Format(
                AppResources.AiDownloadRateFormat,
                ByteSize.Describe((long)progress.BytesPerSecond)));
        }

        if (progress.Remaining is { } remaining && remaining >= ShortestWorthShowing)
        {
            parts.Add(string.Format(AppResources.AiDownloadEtaFormat, DescribeDuration(remaining)));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Rounded up and to one unit. "1 h 3 min" is a false promise measured against a rate that is
    /// two seconds old; "~1 h" is honest about how much of it is a guess.
    /// </summary>
    private static string DescribeDuration(TimeSpan span)
    {
        if (span.TotalMinutes < 1)
        {
            return $"{Math.Ceiling(span.TotalSeconds):0} {AppResources.SecondsShort}";
        }

        return span.TotalHours < 1
            ? $"{Math.Ceiling(span.TotalMinutes):0} {AppResources.MinutesShort}"
            : $"{Math.Ceiling(span.TotalHours):0} {AppResources.HoursShort}";
    }
}
