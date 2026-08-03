using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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

        _index.ProgressChanged += OnIndexProgressChanged;
    }

    public void Load()
    {
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

        Models.Clear();
        foreach (var model in AiModelCatalog.All)
        {
            Models.Add(new AiModelItemViewModel(
                model,
                _downloads,
                capabilities,
                recommended.Contains(model.Id),
                OnModelChanged));
        }

        RefreshIndexStatus(_index.Progress);
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
public partial class AiModelItemViewModel : ObservableObject
{
    private readonly IModelDownloadService _downloads;
    private readonly Action<AiModelItemViewModel> _onChanged;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private ModelInstallState _state;

    [ObservableProperty] private double _progressFraction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public AiModelItemViewModel(
        AiModelDescriptor descriptor,
        IModelDownloadService downloads,
        DeviceCapabilities capabilities,
        bool isRecommended,
        Action<AiModelItemViewModel> onChanged)
    {
        Descriptor = descriptor;
        _downloads = downloads;
        _onChanged = onChanged;

        IsRecommended = isRecommended;
        CanRunHere = descriptor.MinTier <= capabilities.Tier && descriptor.RequiredRamMb <= capabilities.TotalRamMb;
        SizeText = ByteSize.Describe(descriptor.TotalSizeBytes);
        State = downloads.GetState(descriptor);
    }

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

    public bool CanDownload => CanRunHere && State is ModelInstallState.NotInstalled or ModelInstallState.Corrupt;

    public bool CanDelete => State is ModelInstallState.Installed or ModelInstallState.Corrupt;

    public string StateText => State switch
    {
        ModelInstallState.Installed => AppResources.AiStateInstalled,
        ModelInstallState.Downloading => AppResources.AiStateDownloading,
        ModelInstallState.Corrupt => AppResources.AiStateCorrupt,
        _ => AppResources.AiStateNotInstalled,
    };

    [RelayCommand]
    private async Task DownloadAsync()
    {
        ErrorMessage = string.Empty;
        State = ModelInstallState.Downloading;
        ProgressFraction = 0;

        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ModelDownloadProgress>(p => ProgressFraction = p.Fraction);
            var succeeded = await _downloads.DownloadAsync(Descriptor, progress, _cts.Token);

            if (!succeeded)
            {
                ErrorMessage = AppResources.AiDownloadFailed;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelling leaves the partial file in place, so the next attempt resumes.
        }
        catch (Exception)
        {
            // Any network failure reads the same to the user, and there is nowhere to report it to.
            ErrorMessage = AppResources.AiDownloadFailed;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            State = _downloads.GetState(Descriptor);
            _onChanged(this);
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void Delete()
    {
        _downloads.Delete(Descriptor);
        ErrorMessage = string.Empty;
        ProgressFraction = 0;
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
