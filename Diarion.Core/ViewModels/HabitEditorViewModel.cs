using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

[QueryProperty(nameof(HabitId), "HabitId")]
public partial class HabitEditorViewModel : BaseViewModel
{
    private readonly IHabitService _habitService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;

    [ObservableProperty]
    private string? _habitId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isDaily = true;

    [ObservableProperty]
    private bool _isSpecificDays;

    [ObservableProperty]
    private bool _isTimesPerWeek;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimesPerWeekText))]
    private int _timesPerWeek = 3;

    public string TimesPerWeekText => string.Format(AppResources.HabitScheduleTimesPerWeekFormat, TimesPerWeek);

    [ObservableProperty]
    private bool _reminderEnabled;

    [ObservableProperty]
    private TimeSpan _reminderTime = new(9, 0, 0);

    [ObservableProperty]
    private bool _isExisting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = string.Empty;

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public ObservableCollection<WeekdayToggle> Weekdays { get; } = new();

    private Guid _editingId = Guid.Empty;
    private DateTime _createdAt = DateTime.Today;
    private int _order = int.MaxValue;

    public HabitEditorViewModel(
        IHabitService habitService,
        INavigationService navigationService,
        IDialogService dialogService,
        INotificationService notificationService)
    {
        _habitService = habitService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        Title = AppResources.HabitEditorTitle;
        BuildWeekdays();
    }

    private void BuildWeekdays()
    {
        Weekdays.Clear();
        foreach (var toggle in WeekdayToggle.BuildMondayFirst())
        {
            Weekdays.Add(toggle);
        }
    }

    partial void OnHabitIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && Guid.TryParse(value, out var id))
        {
            _ = LoadAsync(id);
        }
    }

    private async Task LoadAsync(Guid id)
    {
        var def = await _habitService.GetHabitDefinitionByIdAsync(id);
        if (def == null) return;

        _editingId = def.Id;
        _createdAt = def.CreatedAt;
        _order = def.Order;
        IsExisting = true;

        Name = (string.IsNullOrEmpty(def.Name) ? HabitLocalization.ResolveName(def) : def.Name) ?? string.Empty;

        var schedule = def.Schedule ?? new RecurrenceRule();
        IsTimesPerWeek = def.Target != null;
        IsSpecificDays = !IsTimesPerWeek && schedule.Kind == RecurrenceKind.Weekly;
        IsDaily = !IsTimesPerWeek && !IsSpecificDays;
        if (def.Target?.TimesPerWeek is >= 1 and <= 7) TimesPerWeek = def.Target.TimesPerWeek;

        ReminderEnabled = def.ReminderTime.HasValue;
        if (def.ReminderTime.HasValue) ReminderTime = def.ReminderTime.Value;

        var selected = new HashSet<int>(schedule.DaysOfWeek ?? new List<int>());
        foreach (var day in Weekdays)
        {
            day.IsSelected = selected.Contains(day.DayOfWeek);
        }
    }

    [RelayCommand]
    private void SetDaily() => SelectType(daily: true, specific: false, weekly: false);

    [RelayCommand]
    private void SetSpecificDays() => SelectType(daily: false, specific: true, weekly: false);

    [RelayCommand]
    private void SetTimesPerWeek() => SelectType(daily: false, specific: false, weekly: true);

    private void SelectType(bool daily, bool specific, bool weekly)
    {
        IsDaily = daily;
        IsSpecificDays = specific;
        IsTimesPerWeek = weekly;
    }

    [RelayCommand]
    private void IncrementTimes()
    {
        if (TimesPerWeek < 7) TimesPerWeek++;
    }

    [RelayCommand]
    private void DecrementTimes()
    {
        if (TimesPerWeek > 1) TimesPerWeek--;
    }

    [RelayCommand]
    private void ToggleWeekday(WeekdayToggle? day)
    {
        if (day == null) return;
        day.IsSelected = !day.IsSelected;
        SelectType(daily: false, specific: true, weekly: false); // choosing days implies specific-days
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidationMessage = string.Empty;

        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationMessage = AppResources.HabitEditorNameRequired;
            return;
        }

        var schedule = new RecurrenceRule();
        CompletionTarget? target = null;
        if (IsTimesPerWeek)
        {
            // A quota is open on every day; the target carries the "how many".
            schedule.Kind = RecurrenceKind.Daily;
            target = new CompletionTarget { TimesPerWeek = Math.Clamp(TimesPerWeek, 1, 7) };
        }
        else if (IsSpecificDays)
        {
            var days = Weekdays.Where(w => w.IsSelected).Select(w => w.DayOfWeek).OrderBy(x => x).ToList();
            if (days.Count == 0)
            {
                ValidationMessage = AppResources.HabitEditorDaysRequired;
                return;
            }
            schedule.Kind = RecurrenceKind.Weekly;
            schedule.DaysOfWeek = days;
        }
        else
        {
            schedule.Kind = RecurrenceKind.Daily;
        }

        TimeSpan? reminder = ReminderEnabled ? ReminderTime : null;
        Guid savedId;

        if (IsExisting)
        {
            var def = await _habitService.GetHabitDefinitionByIdAsync(_editingId);
            if (def == null)
            {
                await _navigationService.NavigateBackAsync();
                return;
            }

            def.Name = name;
            def.ResourceKey = string.Empty; // an edited name overrides any built-in localization key
            def.Schedule = schedule;
            def.Target = target;
            def.ReminderTime = reminder;
            await _habitService.UpdateHabitDefinitionAsync(def);
            savedId = def.Id;
        }
        else
        {
            var def = new HabitDefinition
            {
                Name = name,
                Schedule = schedule,
                Target = target,
                ReminderTime = reminder,
                CreatedAt = DateTime.Today
            };
            await _habitService.AddHabitDefinitionAsync(def);
            savedId = def.Id;
        }

        await ApplyReminderAsync(savedId, name, reminder, schedule);

        await _navigationService.NavigateBackAsync();
    }

    private async Task ApplyReminderAsync(Guid habitId, string name, TimeSpan? reminder, RecurrenceRule schedule)
    {
        if (reminder.HasValue)
        {
            await _notificationService.RequestPermissionsAsync();
            // A quota habit is genuinely Daily now, so it keeps getting the daily reminder it always got.
            var weekdays = schedule.Kind == RecurrenceKind.Weekly ? schedule.DaysOfWeek : null;
            _notificationService.ScheduleHabitReminder(habitId, name, reminder.Value, weekdays);
        }
        else
        {
            _notificationService.CancelHabitReminder(habitId);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!IsExisting)
        {
            await _navigationService.NavigateBackAsync();
            return;
        }

        bool confirm = await _dialogService.ShowConfirmationAsync(
            AppResources.DeleteHabitConfirmTitle,
            string.Format(AppResources.DeleteHabitConfirmMessage, Name),
            AppResources.DeleteConfirmYes,
            AppResources.DeleteConfirmNo);

        if (!confirm) return;

        await _habitService.DeleteHabitDefinitionAsync(_editingId, DateTime.Today);
        _notificationService.CancelHabitReminder(_editingId);
        await _navigationService.NavigateBackAsync();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _navigationService.NavigateBackAsync();
    }
}
