using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class QuickMenuViewModel : ObservableObject
{
    private readonly IMenuConfigurationService _menuConfigurationService;
    private readonly IProfileService _profileService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<QuickMenuItem> QuickMenuItems { get; } = new();

    private QuickMenuItem? _draggedMenuItem;

    public QuickMenuViewModel(
        IMenuConfigurationService menuConfigurationService,
        IProfileService profileService,
        INavigationService navigationService)
    {
        _menuConfigurationService = menuConfigurationService;
        _profileService = profileService;
        _navigationService = navigationService;
    }

    public void Initialize()
    {
        InitQuickMenuDefault();
        _ = LoadQuickMenuAsync();
    }

    private void InitQuickMenuDefault()
    {
        var defaultItems = _menuConfigurationService.GetDefaultMenuItems();
        foreach (var item in defaultItems)
        {
            switch (item.Id)
            {
                case "Reading": item.Command = OpenReadingTrackerCommand; break;
                case "Moments": item.Command = OpenHappyMomentsCommand; break;
                case "Deeds": item.Command = OpenGoodDeedsCommand; break;
                case "Habits": item.Command = OpenHabitTrackerCommand; break;
                case "Wishlist": item.Command = OpenWishlistCommand; break;
                case "Finance": item.Command = OpenFinanceCommand; break;
            }
        }

        QuickMenuItems.Clear();
        foreach (var item in defaultItems)
        {
            QuickMenuItems.Add(item);
        }
    }

    private async Task LoadQuickMenuAsync()
    {
        var profile = await _profileService.GetUserProfileAsync();
        
        if (profile.QuickMenuOrder != null && profile.QuickMenuOrder.Count > 0)
        {
            var orderedItems = new List<QuickMenuItem>();
            var currentItems = QuickMenuItems.ToList();
            
            foreach (var id in profile.QuickMenuOrder)
            {
                var item = currentItems.FirstOrDefault(x => x.Id == id);
                if (item != null)
                {
                    orderedItems.Add(item);
                    currentItems.Remove(item);
                }
            }

            foreach (var item in currentItems)
            {
                orderedItems.Add(item);
            }

            QuickMenuItems.Clear();
            foreach (var item in orderedItems)
            {
                QuickMenuItems.Add(item);
            }
        }
    }

    [RelayCommand]
    public void DragMenuStarting(QuickMenuItem item)
    {
        _draggedMenuItem = item;
    }

    [RelayCommand]
    public void DropMenuCompleted()
    {
        _draggedMenuItem = null;
    }

    [RelayCommand]
    public async Task ReorderMenuAsync(QuickMenuItem targetItem)
    {
        if (_draggedMenuItem == null || targetItem == null || _draggedMenuItem == targetItem)
            return;

        int oldIndex = QuickMenuItems.IndexOf(_draggedMenuItem);
        int newIndex = QuickMenuItems.IndexOf(targetItem);

        if (oldIndex < 0 || newIndex < 0)
            return;

        QuickMenuItems.Move(oldIndex, newIndex);

        var profile = await _profileService.GetUserProfileAsync();
        profile.QuickMenuOrder = QuickMenuItems.Select(x => x.Id).ToList();
        await _profileService.SaveUserProfileAsync(profile);
    }

    [RelayCommand]
    private async Task OpenHabitTrackerAsync() => await _navigationService.NavigateToAsync("HabitTracker");

    [RelayCommand]
    private async Task OpenReadingTrackerAsync() => await _navigationService.NavigateToAsync("ReadingTracker");

    [RelayCommand]
    private async Task OpenHappyMomentsAsync() => await _navigationService.NavigateToAsync("HappyMoments");

    [RelayCommand]
    private async Task OpenGoodDeedsAsync() => await _navigationService.NavigateToAsync("GoodDeeds");

    [RelayCommand]
    private async Task OpenWishlistAsync() => await _navigationService.NavigateToAsync("Wishlist");

    [RelayCommand]
    private async Task OpenFinanceAsync() => await _navigationService.NavigateToAsync("Finance");
}
