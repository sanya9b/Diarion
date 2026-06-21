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

    [ObservableProperty]
    private UserProfile _profile = new();

    [ObservableProperty]
    private GenderItem? _selectedGenderItem;

    public List<GenderItem> GenderList { get; } = new()
    {
        new(GenderType.NotSpecified, Diarion.Resources.Localization.AppResources.GenderNotSpecified),
        new(GenderType.Female, Diarion.Resources.Localization.AppResources.GenderFemale),
        new(GenderType.Male, Diarion.Resources.Localization.AppResources.GenderMale),
        new(GenderType.Other, Diarion.Resources.Localization.AppResources.GenderOther)
    };

    public ProfileViewModel(IProfileService profileService, IBackupService backupService)
    {
        _profileService = profileService;
        _backupService = backupService;
        Title = Diarion.Resources.Localization.AppResources.ProfileMenuTitle;
    }

    public async Task LoadProfileAsync()
    {
        IsBusy = true;
        Profile = await _profileService.GetUserProfileAsync();
        SelectedGenderItem = GenderList.FirstOrDefault(g => g.Value == Profile.Gender) ?? GenderList[0];
        IsBusy = false;
    }

    partial void OnSelectedGenderItemChanged(GenderItem? value)
    {
        if (value != null && Profile != null)
        {
            Profile.Gender = value.Value;
            // Автоматично пропонуємо увімкнути календар, якщо вибрано "Жіноча", а він ще не активований
            if (value.Value == GenderType.Female && !Profile.IsMenstrualTrackingEnabled && Profile.LastPeriodStartDate == null)
            {
                Profile.IsMenstrualTrackingEnabled = true;
            }
        }
    }

    [RelayCommand]
    public void OpenMenu()
    {
        Microsoft.Maui.Controls.Shell.Current.FlyoutIsPresented = true;
    }

    [RelayCommand]
    public async Task SaveProfileAsync()
    {
        IsBusy = true;
        await _profileService.SaveUserProfileAsync(Profile);
        IsBusy = false;
        
        await Shell.Current.DisplayAlertAsync(
            Title, 
            Diarion.Resources.Localization.AppResources.ProfileSavedMessage, 
            Diarion.Resources.Localization.AppResources.OkButtonLabel);
    }

    [RelayCommand]
    public async Task ExportBackupAsync()
    {
        bool success = await _backupService.ExportBackupAsync();
        if (success)
        {
            await Shell.Current.DisplayAlertAsync(
                Diarion.Resources.Localization.AppResources.BackupTitle ?? "Backup", 
                Diarion.Resources.Localization.AppResources.BackupExportSuccess ?? "Backup created successfully.", 
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
        }
    }

    [RelayCommand]
    public async Task ImportBackupAsync()
    {
        bool confirm = await Shell.Current.DisplayAlertAsync(
            Diarion.Resources.Localization.AppResources.BackupTitle ?? "Restore Backup", 
            Diarion.Resources.Localization.AppResources.BackupImportWarning ?? "This will overwrite your current data. Are you sure?", 
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes, 
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
            
        if (!confirm) return;

        bool success = await _backupService.ImportBackupAsync();
        if (success)
        {
            await Shell.Current.DisplayAlertAsync(
                Diarion.Resources.Localization.AppResources.BackupTitle ?? "Backup", 
                Diarion.Resources.Localization.AppResources.BackupImportSuccess ?? "Backup restored. Please restart the app.", 
                Diarion.Resources.Localization.AppResources.OkButtonLabel);
        }
    }
}
