using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly INotificationService _notificationService;

    /// <summary>Invoked when onboarding is finished so the host can dismiss it and mark it complete.</summary>
    public Action? Completed { get; set; }

    [ObservableProperty] private bool _isDailyReminderEnabled = true;
    [ObservableProperty] private TimeSpan _dailyReminderTime = new(20, 0, 0);

    public OnboardingViewModel(IProfileService profileService, INotificationService notificationService)
    {
        _profileService = profileService;
        _notificationService = notificationService;
    }

    [RelayCommand]
    public async Task CompleteAsync()
    {
        var profile = await _profileService.GetUserProfileAsync();
        profile.IsDailyReminderEnabled = IsDailyReminderEnabled;
        profile.DailyReminderTime = DailyReminderTime;
        await _profileService.SaveUserProfileAsync(profile);

        if (IsDailyReminderEnabled)
        {
            await _notificationService.RequestPermissionsAsync();
            _notificationService.ScheduleDailyJournalReminder(DailyReminderTime);
        }
        else
        {
            _notificationService.CancelDailyJournalReminder();
        }

        Completed?.Invoke();
    }
}
