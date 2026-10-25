using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Backup;

namespace Diarion.ViewModels;

/// <summary>One choice in the "how often" picker.</summary>
public sealed record BackupIntervalOption(int Days, string Label);

/// <summary>
/// The "automatic copies" card in Settings → Data. Its own view model, like the subscription card:
/// the profile view model is already the size it is.
/// </summary>
/// <remarks>
/// Switching the feature on is a short conversation — a passphrase, twice — and the switch only stays
/// on if the conversation finishes. Backing out flips it back, so the switch never claims a backup is
/// being kept that is not. Copies go to the phone itself unless another folder is chosen (owner,
/// 2026-10-23); only where the platform has no such folder does switching on open a picker first. The
/// first copy is made at once, which is both the proof that it works and the one backup most people
/// would otherwise never get round to making.
/// </remarks>
public partial class ScheduledBackupViewModel : ObservableObject
{
    private readonly IScheduledBackupService _service;
    private readonly IDialogService _dialogs;

    /// <summary>True while the switch and picker are being set from stored settings, not by the user.</summary>
    private bool _applying;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private string _folderText = string.Empty;

    /// <summary>True while copies go somewhere other than the phone and the phone is an option — the way back.</summary>
    [ObservableProperty]
    private bool _canUseDeviceFolder;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private BackupIntervalOption? _selectedInterval;

    public IReadOnlyList<BackupIntervalOption> Intervals { get; }

    public bool IsSupported => _service.IsSupported;

    public bool IsUnsupported => !_service.IsSupported;

    public ScheduledBackupViewModel(IScheduledBackupService service, IDialogService dialogs)
    {
        _service = service;
        _dialogs = dialogs;
        Intervals = ScheduledBackupSettings.IntervalChoices
            .Select(days => new BackupIntervalOption(days, IntervalLabel(days)))
            .ToList();
    }

    /// <summary>Reads the stored settings into the card. Called on every appearance of the settings screen.</summary>
    public void Load()
    {
        var settings = _service.Settings;

        _applying = true;
        try
        {
            IsEnabled = settings.IsEnabled;
            SelectedInterval = Intervals.FirstOrDefault(i => i.Days == settings.IntervalDays)
                               ?? Intervals.First(i => i.Days == ScheduledBackupSettings.DefaultIntervalDays);
        }
        finally
        {
            _applying = false;
        }

        RefreshTexts();
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
            _service.Disable();
            RefreshTexts();
        }
    }

    partial void OnSelectedIntervalChanged(BackupIntervalOption? value)
    {
        if (_applying || value is null) return;

        _service.SetInterval(value.Days);
    }

    private async Task EnableAsync()
    {
        try
        {
            // The phone itself when it has a folder for copies — no picker, nothing to grant. Only a
            // platform without one asks where.
            var folder = _service.DeviceFolder ?? await _service.PickFolderAsync();
            if (folder is null)
            {
                Revert();
                return;
            }

            var passphrase = await AskPassphraseAsync();
            if (passphrase is null)
            {
                Revert();
                return;
            }

            _service.Enable(folder, passphrase, SelectedInterval?.Days ?? ScheduledBackupSettings.DefaultIntervalDays);
            RefreshTexts();
        }
        catch (Exception ex)
        {
            // Started from a property setter, so nothing above would ever see this exception; a
            // picker that fails must not leave the switch on with no folder behind it.
            System.Diagnostics.Debug.WriteLine($"Enabling scheduled backups failed: {ex}");
            Revert();
            await _dialogs.ShowAlertAsync(
                AppResources.AutoBackupTitle,
                AppResources.AutoBackupOutcomeFailed,
                AppResources.OkButtonLabel);
            return;
        }

        await RunNowAsync();
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

    /// <summary>
    /// Asked twice, because this passphrase is typed once and then never again until the day it is
    /// needed — and a typo discovered on that day is the whole diary gone.
    /// </summary>
    private async Task<string?> AskPassphraseAsync()
    {
        var first = await _dialogs.ShowPromptAsync(
            AppResources.BackupPassphraseTitle,
            AppResources.AutoBackupPassphrasePrompt,
            AppResources.OkButtonLabel,
            AppResources.DeleteConfirmNo);

        if (string.IsNullOrWhiteSpace(first)) return null;

        var second = await _dialogs.ShowPromptAsync(
            AppResources.BackupPassphraseTitle,
            AppResources.AutoBackupPassphraseConfirm,
            AppResources.OkButtonLabel,
            AppResources.DeleteConfirmNo);

        if (string.IsNullOrWhiteSpace(second)) return null;

        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            await _dialogs.ShowAlertAsync(
                AppResources.BackupPassphraseTitle,
                AppResources.AutoBackupPassphraseMismatch,
                AppResources.OkButtonLabel);
            return null;
        }

        return first;
    }

    /// <summary>Back from a picked folder to the phone itself.</summary>
    [RelayCommand]
    private void UseDeviceFolder()
    {
        if (_service.DeviceFolder is not { } device) return;

        _service.SetFolder(device);
        RefreshTexts();
    }

    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        var folder = await _service.PickFolderAsync();
        if (folder is null) return;

        _service.SetFolder(folder);
        RefreshTexts();
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        IsBusy = true;
        try
        {
            var outcome = await _service.RunNowAsync();
            RefreshTexts();

            if (outcome != ScheduledBackupOutcome.Success)
            {
                await _dialogs.ShowAlertAsync(
                    AppResources.AutoBackupTitle,
                    OutcomeText(outcome),
                    AppResources.OkButtonLabel);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshTexts()
    {
        var settings = _service.Settings;
        var culture = CultureInfo.CurrentCulture;

        var isDevice = settings.FolderToken == BackupFolderPick.DeviceToken;

        // The phone's folder is named from the service each time rather than from the stored name, so it
        // follows a change of the app's language.
        var name = isDevice ? _service.DeviceFolder?.DisplayName : settings.FolderName;
        FolderText = string.IsNullOrEmpty(name)
            ? AppResources.AutoBackupNoFolder
            : string.Format(culture, AppResources.AutoBackupFolderFormat, name);

        CanUseDeviceFolder = !isDevice && _service.DeviceFolder is not null;

        StatusText = StatusFor(settings, culture);
    }

    /// <summary>
    /// One line about the last attempt. A success names its time; a failure names itself, with
    /// the last good time after it when there is one — "failed, last good copy Tuesday" is the
    /// sentence that tells someone how exposed they are.
    /// </summary>
    public static string StatusFor(ScheduledBackupSettings settings, CultureInfo culture)
    {
        var lastGood = settings.LastRunUtc is DateTime utc
            ? string.Format(culture, AppResources.AutoBackupLastRunFormat, utc.ToLocalTime().ToString("g", culture))
            : null;

        return settings.LastOutcome switch
        {
            ScheduledBackupOutcome.Success or ScheduledBackupOutcome.NotDue or ScheduledBackupOutcome.None
                => lastGood ?? AppResources.AutoBackupNeverRun,
            var failure => lastGood is null ? OutcomeText(failure) : $"{OutcomeText(failure)} {lastGood}"
        };
    }

    public static string OutcomeText(ScheduledBackupOutcome outcome) => outcome switch
    {
        ScheduledBackupOutcome.Success => AppResources.AutoBackupOutcomeSuccess,
        ScheduledBackupOutcome.FolderUnavailable => AppResources.AutoBackupOutcomeFolderUnavailable,
        ScheduledBackupOutcome.NoPassphrase => AppResources.AutoBackupOutcomeNoPassphrase,
        ScheduledBackupOutcome.Disabled => AppResources.AutoBackupNeverRun,
        ScheduledBackupOutcome.NotDue => AppResources.AutoBackupNeverRun,
        _ => AppResources.AutoBackupOutcomeFailed
    };

    private static string IntervalLabel(int days) => days == 1
        ? AppResources.AutoBackupIntervalDaily
        : string.Format(CultureInfo.CurrentCulture, AppResources.AutoBackupIntervalDaysFormat, days);
}
