using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class HabitsSectionViewModel : ObservableObject
{
    private readonly IHabitService _habitService;
    private readonly IDialogService _dialogService;
    private readonly CalendarSectionViewModel _calendarSection;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private DiaryEntryViewModel? _entry;

    [ObservableProperty]
    private bool _isEditHabitsMode;

    private HabitItemViewModel? _draggedHabit;

    public HabitsSectionViewModel(
        IHabitService habitService,
        IDialogService dialogService,
        CalendarSectionViewModel calendarSection,
        INavigationService navigationService)
    {
        _habitService = habitService;
        _dialogService = dialogService;
        _calendarSection = calendarSection;
        _navigationService = navigationService;
    }

    [RelayCommand]
    public void ToggleEditHabitsMode()
    {
        IsEditHabitsMode = !IsEditHabitsMode;
    }

    [RelayCommand]
    public void DragHabitStarting(HabitItemViewModel item)
    {
        _draggedHabit = item;
    }

    [RelayCommand]
    public void DropHabitCompleted()
    {
        _draggedHabit = null;
    }

    [RelayCommand]
    public async Task ReorderHabitsAsync(HabitItemViewModel targetItem)
    {
        if (_draggedHabit == null || targetItem == null || _draggedHabit == targetItem)
            return;

        if (Entry == null) return;

        int oldIndex = Entry.Habits.IndexOf(_draggedHabit);
        int newIndex = Entry.Habits.IndexOf(targetItem);

        if (oldIndex < 0 || newIndex < 0)
            return;

        Entry.Habits.Move(oldIndex, newIndex);

        var orderedIds = Entry.Habits.Select(h => h.HabitId).ToList();
        await _habitService.UpdateHabitDefinitionsOrderAsync(orderedIds);
    }

    [RelayCommand]
    public async Task AddHabitAsync()
    {
        // Opens the editor for a new habit (name + schedule). The daily section refreshes on return
        // because MainPage reloads the day in OnAppearing.
        await _navigationService.NavigateToAsync("HabitEditor");
    }

    [RelayCommand]
    public async Task EditHabitAsync(HabitItemViewModel item)
    {
        if (item == null) return;

        await _navigationService.NavigateToAsync("HabitEditor", new System.Collections.Generic.Dictionary<string, object>
        {
            { "HabitId", item.HabitId.ToString() }
        });
    }

    [RelayCommand]
    public async Task DeleteHabitAsync(HabitItemViewModel item)
    {
        if (item == null || Entry == null) return;
        
        bool confirm = await _dialogService.ShowConfirmationAsync(
            Diarion.Resources.Localization.AppResources.DeleteHabitConfirmTitle,
            string.Format(Diarion.Resources.Localization.AppResources.DeleteHabitConfirmMessage, item.Name),
            Diarion.Resources.Localization.AppResources.DeleteConfirmYes,
            Diarion.Resources.Localization.AppResources.DeleteConfirmNo);
            
        if (!confirm) return;

        await _habitService.DeleteHabitDefinitionAsync(item.HabitId, _calendarSection.GetSelectedDate());
        Entry.Habits.Remove(item);
    }
}
