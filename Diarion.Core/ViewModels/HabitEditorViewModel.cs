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

public partial class WeekdayToggle : ObservableObject
{
    public int DayOfWeek { get; set; } // (int)System.DayOfWeek, 0 = Sunday … 6 = Saturday
    public string ShortName { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

[QueryProperty(nameof(HabitId), "HabitId")]
public partial class HabitEditorViewModel : BaseViewModel
{
    private readonly IHabitService _habitService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string? _habitId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpecificDays))]
    private bool _isDaily = true;

    public bool IsSpecificDays => !IsDaily;

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

    public HabitEditorViewModel(IHabitService habitService, INavigationService navigationService, IDialogService dialogService)
    {
        _habitService = habitService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = AppResources.HabitEditorTitle;
        BuildWeekdays();
    }

    private void BuildWeekdays()
    {
        Weekdays.Clear();
        var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames; // indexed by (int)DayOfWeek
        // Monday-first order.
        foreach (var dow in new[] { 1, 2, 3, 4, 5, 6, 0 })
        {
            Weekdays.Add(new WeekdayToggle { DayOfWeek = dow, ShortName = names[dow] });
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

        var schedule = def.Schedule ?? new HabitSchedule();
        IsDaily = schedule.Type == HabitScheduleType.Daily;
        var selected = new HashSet<int>(schedule.DaysOfWeek ?? new List<int>());
        foreach (var day in Weekdays)
        {
            day.IsSelected = selected.Contains(day.DayOfWeek);
        }
    }

    [RelayCommand]
    private void SetDaily() => IsDaily = true;

    [RelayCommand]
    private void SetSpecificDays() => IsDaily = false;

    [RelayCommand]
    private void ToggleWeekday(WeekdayToggle? day)
    {
        if (day == null) return;
        day.IsSelected = !day.IsSelected;
        IsDaily = false; // choosing days implies a specific-days schedule
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

        var schedule = new HabitSchedule();
        if (IsDaily)
        {
            schedule.Type = HabitScheduleType.Daily;
        }
        else
        {
            var days = Weekdays.Where(w => w.IsSelected).Select(w => w.DayOfWeek).OrderBy(x => x).ToList();
            if (days.Count == 0)
            {
                ValidationMessage = AppResources.HabitEditorDaysRequired;
                return;
            }
            schedule.Type = HabitScheduleType.SpecificDays;
            schedule.DaysOfWeek = days;
        }

        if (IsExisting)
        {
            var def = await _habitService.GetHabitDefinitionByIdAsync(_editingId);
            if (def != null)
            {
                def.Name = name;
                def.ResourceKey = string.Empty; // an edited name overrides any built-in localization key
                def.Schedule = schedule;
                await _habitService.UpdateHabitDefinitionAsync(def);
            }
        }
        else
        {
            await _habitService.AddHabitDefinitionAsync(new HabitDefinition
            {
                Name = name,
                Schedule = schedule,
                CreatedAt = DateTime.Today
            });
        }

        await _navigationService.NavigateBackAsync();
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
        await _navigationService.NavigateBackAsync();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _navigationService.NavigateBackAsync();
    }
}
