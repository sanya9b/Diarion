using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;

namespace Diarion.ViewModels;

public partial class QuickMenuViewModel : ObservableObject
{
    private readonly IMenuConfigurationService _menuConfigurationService;
    private readonly IProfileService _profileService;
    private readonly INavigationService _navigationService;
    private readonly IAiAvailability _aiAvailability;

    public ObservableCollection<QuickMenuItem> QuickMenuItems { get; } = new();

    /// <summary>Every tile the app knows about, in catalogue order, whether shown or not.</summary>
    private readonly List<QuickMenuItem> _catalogue = new();

    private List<string> _savedOrder = new();

    /// <summary>
    /// Starts hidden. The alternative — showing the tile and hiding it a moment later — puts a
    /// button on the screen that vanishes as the user reaches for it.
    /// </summary>
    private bool _isChatAvailable;

    private QuickMenuItem? _draggedMenuItem;

    public QuickMenuViewModel(
        IMenuConfigurationService menuConfigurationService,
        IProfileService profileService,
        INavigationService navigationService,
        IAiAvailability aiAvailability)
    {
        _menuConfigurationService = menuConfigurationService;
        _profileService = profileService;
        _navigationService = navigationService;
        _aiAvailability = aiAvailability;
    }

    public void Initialize()
    {
        InitQuickMenuDefault();
        _ = LoadQuickMenuAsync();
    }

    private void InitQuickMenuDefault()
    {
        _catalogue.Clear();
        foreach (var item in _menuConfigurationService.GetDefaultMenuItems())
        {
            switch (item.Id)
            {
                case MenuConfigurationService.SearchId: item.Command = OpenSearchCommand; break;
                case "Notes": item.Command = OpenNotesCommand; break;
                case "Reading": item.Command = OpenReadingTrackerCommand; break;
                case "Moments": item.Command = OpenHappyMomentsCommand; break;
                case "Deeds": item.Command = OpenGoodDeedsCommand; break;
                case "Habits": item.Command = OpenHabitTrackerCommand; break;
                case "Wishlist": item.Command = OpenWishlistCommand; break;
                case "Finance": item.Command = OpenFinanceCommand; break;
                case MenuConfigurationService.AiChatId: item.Command = OpenAiChatCommand; break;
            }

            _catalogue.Add(item);
        }

        Rebuild();
    }

    private async Task LoadQuickMenuAsync()
    {
        var profile = await _profileService.GetUserProfileAsync();
        _savedOrder = profile.QuickMenuOrder ?? new List<string>();
        _isChatAvailable = await _aiAvailability.CanGenerateAsync();

        Rebuild();
    }

    /// <summary>
    /// Called when the main page reappears — the only moment either of the two things this strip
    /// depends on can have changed behind its back.
    /// </summary>
    /// <remarks>
    /// The generative model is installed on another screen, so that is the only moment the chat tile
    /// can honestly appear. The saved order is written by two screens the user leaves to reach: the
    /// onboarding module picker, and a drag on the strip itself. Both comparisons guard the same
    /// thing — <see cref="Rebuild"/> clears and refills the collection, which is a visible flicker if
    /// it runs on every return to the home screen for no reason.
    /// </remarks>
    public async Task RefreshAsync()
    {
        var profile = await _profileService.GetUserProfileAsync();
        var order = profile.QuickMenuOrder ?? new List<string>();
        var available = await _aiAvailability.CanGenerateAsync();

        if (available == _isChatAvailable && order.SequenceEqual(_savedOrder))
        {
            return;
        }

        _isChatAvailable = available;
        _savedOrder = order;
        Rebuild();
    }

    private void Rebuild()
    {
        var pending = _catalogue
            .Where(item => item.Id != MenuConfigurationService.AiChatId || _isChatAvailable)
            .ToList();

        var ordered = new List<QuickMenuItem>();
        foreach (var id in _savedOrder)
        {
            var item = pending.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                ordered.Add(item);
                pending.Remove(item);
            }
        }

        // Whatever the saved order never knew about goes last: a tile added by an update, or the
        // chat tile the first time a model is installed.
        ordered.AddRange(pending);

        QuickMenuItems.Clear();
        foreach (var item in ordered)
        {
            QuickMenuItems.Add(item);
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

        _savedOrder = QuickMenuItems.Select(x => x.Id).ToList();

        var profile = await _profileService.GetUserProfileAsync();
        profile.QuickMenuOrder = _savedOrder;
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

    [RelayCommand]
    private async Task OpenNotesAsync() => await _navigationService.NavigateToAsync("Notes");

    [RelayCommand]
    private async Task OpenSearchAsync() => await _navigationService.NavigateToAsync("Search");

    [RelayCommand]
    private async Task OpenAiChatAsync() => await _navigationService.NavigateToAsync("AiChat");
}
