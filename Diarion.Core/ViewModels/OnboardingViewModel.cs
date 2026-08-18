using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// The first-run walkthrough: three screens that explain, one that asks what to keep, one that offers
/// the daily reminder.
/// </summary>
/// <remarks>
/// Steps are an index and a set of <c>IsXStep</c> flags rather than a <c>CarouselView</c>. The five are
/// not five of the same thing — two of them are forms — so a template selector would exist only to
/// deliver swipe, and swipe past a screen with checkboxes on it is a way to lose an answer.
/// </remarks>
public partial class OnboardingViewModel : ObservableObject
{
    public const int WelcomeStep = 0;
    public const int JournalStep = 1;
    public const int InsightsStep = 2;
    public const int ModulesStep = 3;
    public const int ReminderStep = 4;
    public const int StepCount = 5;

    private readonly IProfileService _profileService;
    private readonly INotificationService _notificationService;
    private readonly IOnboardingModuleService _onboardingModuleService;

    /// <summary>Invoked when onboarding is finished so the host can dismiss it and mark it complete.</summary>
    public Action? Completed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcomeStep), nameof(IsJournalStep), nameof(IsInsightsStep))]
    [NotifyPropertyChangedFor(nameof(IsModulesStep), nameof(IsReminderStep))]
    [NotifyPropertyChangedFor(nameof(CanGoBack), nameof(CanGoNext), nameof(CanSkip))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    private int _currentStep;

    [ObservableProperty] private bool _isDailyReminderEnabled = true;
    [ObservableProperty] private TimeSpan _dailyReminderTime = new(20, 0, 0);

    public ObservableCollection<OnboardingModule> DailyEntryModules { get; } = new();
    public ObservableCollection<OnboardingModule> SectionModules { get; } = new();

    /// <summary>The progress strip. Fixed length, so it is built once and only toggled after that.</summary>
    public IReadOnlyList<OnboardingStepDot> StepDots { get; } =
        Enumerable.Range(0, StepCount).Select(i => new OnboardingStepDot { IsActive = i == WelcomeStep }).ToList();

    public bool IsWelcomeStep => CurrentStep == WelcomeStep;
    public bool IsJournalStep => CurrentStep == JournalStep;
    public bool IsInsightsStep => CurrentStep == InsightsStep;
    public bool IsModulesStep => CurrentStep == ModulesStep;
    public bool IsReminderStep => CurrentStep == ReminderStep;

    public bool CanGoBack => CurrentStep > WelcomeStep;
    public bool CanGoNext => CurrentStep < ReminderStep;

    /// <summary>Hidden on the last step, where the primary button already finishes.</summary>
    public bool CanSkip => CurrentStep < ReminderStep;

    public OnboardingViewModel(
        IProfileService profileService,
        INotificationService notificationService,
        IOnboardingModuleService onboardingModuleService)
    {
        _profileService = profileService;
        _notificationService = notificationService;
        _onboardingModuleService = onboardingModuleService;

        foreach (var module in _onboardingModuleService.GetModules())
        {
            var target = module.Group == OnboardingModuleGroup.DailyEntry ? DailyEntryModules : SectionModules;
            target.Add(module);
        }
    }

    partial void OnCurrentStepChanged(int value)
    {
        for (var i = 0; i < StepDots.Count; i++)
        {
            StepDots[i].IsActive = i == value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => CurrentStep++;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStep--;

    /// <summary>Leaves now, keeping whatever was already ticked and asking nothing further.</summary>
    [RelayCommand]
    public Task SkipAsync() => FinishAsync(offerReminder: false);

    [RelayCommand]
    public Task CompleteAsync() => FinishAsync(offerReminder: true);

    private async Task FinishAsync(bool offerReminder)
    {
        var profile = await _profileService.GetUserProfileAsync();

        _onboardingModuleService.Apply(profile, DailyEntryModules.Concat(SectionModules).ToList());

        // Skipping is not the same thing as declining a reminder, but it has to be treated as one:
        // otherwise the tap that asked to be left alone is the tap that raises a notification
        // permission prompt. Off is the profile default anyway, so this only makes it explicit.
        var reminderEnabled = offerReminder && IsDailyReminderEnabled;
        profile.IsDailyReminderEnabled = reminderEnabled;
        profile.DailyReminderTime = DailyReminderTime;
        await _profileService.SaveUserProfileAsync(profile);

        if (reminderEnabled)
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
