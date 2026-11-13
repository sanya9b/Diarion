using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Recovery;

namespace Diarion.ViewModels;

/// <summary>
/// The "iCloud recovery" card in Settings → Data (spec 21). The switch is the consent: nothing is
/// written until it is on, and turning it off asks whether the copy already there should go too.
/// </summary>
public partial class CloudRecoveryViewModel : ObservableObject
{
    private readonly ICloudRecoveryService _service;
    private readonly IDialogService _dialogs;

    /// <summary>True while the switch is being set from stored settings, not by the user.</summary>
    private bool _applying;

    public CloudRecoveryViewModel(ICloudRecoveryService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
    }

    public bool IsSupported => _service.IsSupported;

    public bool IsUnsupported => !_service.IsSupported;

    /// <summary>Signed in to iCloud and the container reachable; read once per appearance, off the UI thread.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnavailable))]
    private bool _isAvailable = true;

    public bool IsUnavailable => IsSupported && !IsAvailable;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Reads the stored settings into the card. Called on every appearance of the settings screen.</summary>
    public async Task LoadAsync()
    {
        if (!IsSupported) return;

        _applying = true;
        try
        {
            IsEnabled = _service.Settings.IsEnabled;
        }
        finally
        {
            _applying = false;
        }

        RefreshTexts();
        IsAvailable = await Task.Run(() => _service.IsAvailableAsync());
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_applying) return;

        if (value)
        {
            _ = EnableAsync();
        }
        else
        {
            _ = DisableAsync();
        }
    }

    private async Task EnableAsync()
    {
        IsBusy = true;
        try
        {
            var outcome = await _service.EnableAsync();
            RefreshTexts();
            if (outcome != CloudRecoveryOutcome.Success)
            {
                await _dialogs.ShowAlertAsync(AppResources.CloudRecoveryTitle, OutcomeText(outcome), AppResources.OkButtonLabel);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Enabling cloud recovery failed: {ex}");
            Revert();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisableAsync()
    {
        var deleteCopy = _service.Settings.LastCopyUtc is not null
            && await _dialogs.ShowConfirmationAsync(
                AppResources.CloudRecoveryTitle,
                AppResources.CloudRecoveryDeleteConfirm,
                AppResources.DeleteConfirmYes,
                AppResources.DeleteConfirmNo);

        await _service.DisableAsync(deleteCopy);
        RefreshTexts();
    }

    /// <summary>Puts the switch back without treating that as the user switching off.</summary>
    private void Revert()
    {
        _applying = true;
        try
        {
            IsEnabled = false;
        }
        finally
        {
            _applying = false;
        }
    }

    [RelayCommand]
    private async Task CopyNowAsync()
    {
        IsBusy = true;
        try
        {
            var outcome = await _service.CopyNowAsync();
            RefreshTexts();
            if (outcome != CloudRecoveryOutcome.Success)
            {
                await _dialogs.ShowAlertAsync(AppResources.CloudRecoveryTitle, OutcomeText(outcome), AppResources.OkButtonLabel);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// For the person who tapped "start fresh" on the first launch and regrets it, or who wants a
    /// phone back the way another one left it. Replaces everything here, so it asks first.
    /// </summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        var copy = await Task.Run(() => _service.FindCopyAsync(TimeSpan.FromSeconds(5)));
        if (copy is null)
        {
            await _dialogs.ShowAlertAsync(AppResources.CloudRecoveryTitle, AppResources.CloudRecoveryStatusNever, AppResources.OkButtonLabel);
            return;
        }

        var confirm = await _dialogs.ShowConfirmationAsync(
            AppResources.CloudRecoveryTitle,
            string.Format(CultureInfo.CurrentCulture, AppResources.CloudRecoveryRestoreConfirm, CopyDate(copy.ModifiedUtc)),
            AppResources.DeleteConfirmYes,
            AppResources.DeleteConfirmNo);
        if (!confirm) return;

        IsBusy = true;
        try
        {
            var outcome = await Task.Run(() => _service.RestoreAsync());
            RefreshTexts();
            await _dialogs.ShowAlertAsync(AppResources.CloudRecoveryTitle, RestoreText(outcome), AppResources.OkButtonLabel);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteCopyAsync()
    {
        var confirm = await _dialogs.ShowConfirmationAsync(
            AppResources.CloudRecoveryTitle,
            AppResources.CloudRecoveryDeleteConfirm,
            AppResources.DeleteConfirmYes,
            AppResources.DeleteConfirmNo);
        if (!confirm) return;

        IsBusy = true;
        try
        {
            await _service.DisableAsync(deleteCopy: true);
            Revert();
            RefreshTexts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshTexts()
    {
        var settings = _service.Settings;
        StatusText = settings.LastOutcome == CloudRecoveryOutcome.Failed
            ? AppResources.CloudRecoveryStatusFailed
            : settings.LastCopyUtc is DateTime last
                ? string.Format(CultureInfo.CurrentCulture, AppResources.CloudRecoveryStatusFormat, CopyDate(last))
                : AppResources.CloudRecoveryStatusNever;
    }

    /// <summary>The copy's time in the user's words: a date with the time, local.</summary>
    public static string CopyDate(DateTime? utc) =>
        utc is null ? "—" : utc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public static string OutcomeText(CloudRecoveryOutcome outcome) => outcome switch
    {
        CloudRecoveryOutcome.Success => AppResources.CloudRecoveryTitle,
        CloudRecoveryOutcome.Unavailable => AppResources.CloudRecoveryUnavailable,
        CloudRecoveryOutcome.Disabled => AppResources.CloudRecoveryStatusNever,
        _ => AppResources.CloudRecoveryStatusFailed
    };

    public static string RestoreText(RecoveryRestoreOutcome outcome) => outcome switch
    {
        RecoveryRestoreOutcome.Restored => AppResources.RecoveryRestored,
        RecoveryRestoreOutcome.NotFound => AppResources.CloudRecoveryStatusNever,
        RecoveryRestoreOutcome.DownloadFailed => AppResources.RecoveryOutcomeDownloadFailed,
        RecoveryRestoreOutcome.KeyMismatch => AppResources.RecoveryOutcomeKeyMismatch,
        RecoveryRestoreOutcome.NewerSchema => AppResources.BackupNewerSchema,
        _ => AppResources.RecoveryOutcomeFailed
    };
}
