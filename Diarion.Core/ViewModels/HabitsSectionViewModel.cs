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
    
    [ObservableProperty]
    private DiaryEntryViewModel? _entry;

    [ObservableProperty]
    private bool _isEditHabitsMode;

    private HabitItemViewModel? _draggedHabit;

    public HabitsSectionViewModel(
        IHabitService habitService,
        IDialogService dialogService,
        CalendarSectionViewModel calendarSection)
    {
        _habitService = habitService;
        _dialogService = dialogService;
        _calendarSection = calendarSection;
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
        string result = await _dialogService.ShowPromptAsync(
            Diarion.Resources.Localization.AppResources.AddHabitPromptTitle,
            Diarion.Resources.Localization.AppResources.AddHabitPromptMessage);
            
        if (!string.IsNullOrWhiteSpace(result))
        {
            var def = new HabitDefinition { Name = result.Trim(), CreatedAt = _calendarSection.GetSelectedDate() };
            await _habitService.AddHabitDefinitionAsync(def);
            
            if (Entry != null)
            {
                Entry.Habits.Add(new HabitItemViewModel(new HabitItem { HabitId = def.Id, Name = def.Name }));
            }
        }
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
