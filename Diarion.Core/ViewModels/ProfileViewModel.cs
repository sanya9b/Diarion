using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Microsoft.Maui.Controls;

namespace Diarion.ViewModels;

public record GenderItem(GenderType Value, string DisplayName);

public partial class ProfileViewModel : BaseViewModel
{
    private readonly IProfileService _profileService;
    private readonly IBackupService _backupService;
    private readonly IAppLockService _appLockService;
    private readonly IBiometricService _biometricService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;
    private readonly IExportService _exportService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private UserProfile _profile = new();

    [ObservableProperty]
    private GenderItem? _selectedGenderItem;

    // Settings tabs: 0 = Profile, 1 = Screen, 2 = Data.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileTab))]
    [NotifyPropertyChangedFor(nameof(IsScreenTab))]
    [NotifyPropertyChangedFor(nameof(IsDataTab))]
    private int _selectedTabIndex;

    public bool IsProfileTab => SelectedTabIndex == 0;
    public bool IsScreenTab => SelectedTabIndex == 1;
    public bool IsDataTab => SelectedTabIndex == 2;

    [RelayCommand]
    private void SelectTab(string index)
    {
        if (int.TryParse(index, out var i))
            SelectedTabIndex = i;
    }

    public List<GenderItem> GenderList { get; } = new()
    {
        new(GenderType.NotSpecified, Diarion.Resources.Localization.AppResources.GenderNotSpecified),
        new(GenderType.Female, Diarion.Resources.Localization.AppResources.GenderFemale),
        new(GenderType.Male, Diarion.Resources.Localization.AppResources.GenderMale),
        new(GenderType.Other, Diarion.Resources.Localization.AppResources.GenderOther)
    };

    public ProfileViewModel(
        IProfileService profileService,
        IBackupService backupService,
        IAppLockService appLockService,
        IBiometricService biometricService,
        IDialogService dialogService,
        INotificationService notificationService,
        IExportService exportService,
        INavigationService navigationService)
    {
        _profileService = profileService;
        _backupService = backupService;
        _appLockService = appLockService;
        _biometricService = biometricService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _exportService = exportService;
        _navigationService = navigationService;
        Title = Diarion.Resources.Localization.AppResources.ProfileMenuTitle;
    }

    public async Task LoadProfileAsync()
    {
        IsBusy = true;
        Profile = await _profileService.GetUserProfileAsync();
        SelectedGenderItem = GenderList.FirstOrDefault(g => g.Value == Profile.Gender) ?? GenderList[0];

        NotifyLockState();

        IsBusy = false;
    }

    // ---- App lock (PIN + optional biometrics) ----

    public bool IsLockEnabled => _appLockService.IsLockEnabled;
    public bool IsLockDisabled => !_appLockService.IsLockEnabled;

    public bool IsBiometricEnabled
    {
        get => _appLockService.IsBiometricEnabled;
        set
        {
            if (_appLockService.IsBiometricEnabled == value) return;

            if (value)
            {
                _ = EnableBiometricAsync();
            }
            else
            {
                _appLockService.IsBiometricEnabled = false;
                OnPropertyChanged();
            }
        }
    }

    private async Task EnableBiometricAsync()
    {
        var available = await _biometricService.IsAvailableAsync();
        if (!available)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.BiometricErrorTitle,
                Diarion.Resources.Localization.AppResources.BiometricErrorMessage,
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
            OnPropertyChanged(nameof(IsBiometricEnabled)); // revert the switch in the UI
            return;
        }

        _appLockService.IsBiometricEnabled = true;
        OnPropertyChanged(nameof(IsBiometricEnabled));
    }

    [RelayCommand]
    public async Task SetPinAsync()
    {
        // Full-screen keypad flow (verify current when changing → enter new → repeat) instead of
        // the plain OS prompt dialogs. Lock state refreshes on return via ProfilePage.OnAppearing.
        await _navigationService.NavigateToAsync("PinSetup");
    }

    [RelayCommand]
    public async Task OpenPromptLibraryAsync()
    {
        await _navigationService.NavigateToAsync("PromptLibrary");
    }

    [RelayCommand]
    public async Task RemovePinAsync()
    {
        var confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.AppLockTitle,
            Diarion.Resources.Localization.AppResources.RemovePinConfirmMessage,
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);

        if (!confirm) return;

        _appLockService.RemovePin();
        NotifyLockState();
    }

    private void NotifyLockState()
    {
        OnPropertyChanged(nameof(IsLockEnabled));
        OnPropertyChanged(nameof(IsLockDisabled));
        OnPropertyChanged(nameof(IsBiometricEnabled));
    }

    partial void OnSelectedGenderItemChanged(GenderItem? value)
    {
        if (value != null && Profile != null)
        {
            Profile.Gender = value.Value;
            // Автоматично пропонуємо увімкнути календар, якщо вибрано "Жіноча", а він ще не активований
            if (value.Value == GenderType.Female && !Profile.IsMenstrualTrackingEnabled)
            {
                Profile.IsMenstrualTrackingEnabled = true;
            }
        }
    }

    [RelayCommand]
    public void OpenMenu()
    {
        _ = _navigationService.OpenFlyoutAsync();
    }

    [RelayCommand]
    public async Task SaveProfileAsync()
    {
        IsBusy = true;
        await _profileService.SaveUserProfileAsync(Profile);
        await ApplyDailyReminderAsync();
        IsBusy = false;

        await _dialogService.ShowAlertAsync(
            Title,
            Diarion.Resources.Localization.AppResources.ProfileSavedMessage,
            Diarion.Resources.Localization.AppResources.OkButtonLabel);
    }

    private async Task ApplyDailyReminderAsync()
    {
        if (Profile.IsDailyReminderEnabled)
        {
            await _notificationService.RequestPermissionsAsync();
            _notificationService.ScheduleDailyJournalReminder(Profile.DailyReminderTime);
        }
        else
        {
            _notificationService.CancelDailyJournalReminder();
        }
    }

    [RelayCommand]
    public async Task ExportBackupAsync()
    {
        bool success = await _backupService.ExportBackupAsync();
        if (success)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.BackupTitle ?? "Backup",
                Diarion.Resources.Localization.AppResources.BackupExportSuccess ?? "Backup created successfully.",
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
        }
    }

    [RelayCommand]
    public async Task ImportBackupAsync()
    {
        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.BackupTitle ?? "Restore Backup",
            Diarion.Resources.Localization.AppResources.BackupImportWarning ?? "This will overwrite your current data. Are you sure?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
            
        if (!confirm) return;

        bool success = await _backupService.ImportBackupAsync();
        if (success)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.BackupTitle ?? "Backup",
                Diarion.Resources.Localization.AppResources.BackupImportSuccess ?? "Backup restored. Please restart the app.",
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
        }
    }

    [RelayCommand]
    public async Task ExportDataAsync(string format)
    {
        var exportFormat = (format ?? string.Empty).ToLowerInvariant() switch
        {
            "csv" => ExportFormat.Csv,
            "markdown" => ExportFormat.Markdown,
            _ => ExportFormat.Json
        };

        var success = await _exportService.ExportAndShareAsync(exportFormat);
        if (success)
        {
            await _dialogService.ShowAlertAsync(
                Diarion.Resources.Localization.AppResources.ExportDataTitle,
                Diarion.Resources.Localization.AppResources.ExportSuccessMessage,
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
        }
    }

    [RelayCommand]
    public async Task ClearAllDataAsync()
    {
        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.ClearAllDataConfirmTitle ?? "Warning",
            Diarion.Resources.Localization.AppResources.ClearAllDataConfirmMsg ?? "Are you sure you want to delete all your data?",
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
            
        if (!confirm) return;

        IsBusy = true;
        await _profileService.ClearAllDataAsync();
        
        // Reload empty profile
        await LoadProfileAsync();
        IsBusy = false;

        await _dialogService.ShowAlertAsync(
            Diarion.Resources.Localization.AppResources.ClearAllDataConfirmTitle ?? "Warning",
            Diarion.Resources.Localization.AppResources.ClearAllDataSuccessMsg ?? "All your data has been successfully deleted.",
            Diarion.Resources.Localization.AppResources.OkButtonLabel);
    }
}
