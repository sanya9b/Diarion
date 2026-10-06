using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.ViewModels;

namespace Diarion.Services;

/// <inheritdoc />
public class OnboardingModuleService : IOnboardingModuleService
{
    private readonly IMenuConfigurationService _menuConfigurationService;

    public OnboardingModuleService(IMenuConfigurationService menuConfigurationService)
    {
        _menuConfigurationService = menuConfigurationService;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Built per call, never cached: the titles come out of <see cref="AppResources"/>, and a static
    /// list would freeze whichever language happened to be current when the type was first touched.
    /// <para>
    /// The names are the ones the settings screen already uses, deliberately. Somebody who unchecks
    /// "Food" here and goes looking for it a month later needs to recognise the row when they find it.
    /// </para>
    /// <para>
    /// Two things are not offered. The cycle tracker, because it is gated on a gender this screen never
    /// asks for and its default is off — putting it here would switch on a reproductive-health feature
    /// for someone who only tapped Next. AI chat, because its tile appears only once a model is
    /// installed, so a checkbox for it would promise a screen most phones are never offered.
    /// </para>
    /// </remarks>
    public IReadOnlyList<OnboardingModule> GetModules() => new List<OnboardingModule>
    {
        new()
        {
            Id = "Mood",
            Title = AppResources.BlockMoodLabel,
            Group = OnboardingModuleGroup.DailyEntry,
            SetEnabled = (profile, enabled) => profile.IsMoodBlockVisible = enabled
        },
        new()
        {
            Id = "Sleep",
            Title = AppResources.BlockSleepLabel,
            Group = OnboardingModuleGroup.DailyEntry,
            SetEnabled = (profile, enabled) => profile.IsSleepBlockVisible = enabled
        },
        new()
        {
            Id = "Health",
            Title = AppResources.BlockHealthLabel,
            Group = OnboardingModuleGroup.DailyEntry,
            SetEnabled = (profile, enabled) => profile.IsHealthBlockVisible = enabled
        },
        new()
        {
            Id = "Food",
            Title = AppResources.BlockFoodLabel,
            Group = OnboardingModuleGroup.DailyEntry,
            SetEnabled = (profile, enabled) => profile.IsFoodBlockVisible = enabled
        },
        new()
        {
            Id = "GuidedPrompt",
            Title = AppResources.BlockGuidedPromptLabel,
            Group = OnboardingModuleGroup.DailyEntry,
            SetEnabled = (profile, enabled) => profile.IsGuidedPromptBlockVisible = enabled
        },
        new()
        {
            Id = "Reflection",
            Title = AppResources.BlockReflectionLabel,
            Group = OnboardingModuleGroup.DailyEntry,
            SetEnabled = (profile, enabled) => profile.IsReflectionBlockVisible = enabled
        },
        new()
        {
            // The one module with both halves: a block on the home screen and a screen of its own.
            Id = "Habits",
            Title = AppResources.BlockHabitsLabel,
            Group = OnboardingModuleGroup.DailyEntry,
            SetEnabled = (profile, enabled) => profile.IsHabitsBlockVisible = enabled,
            QuickMenuId = "Habits"
        },

        // Tile-only from here down, in the order the quick menu itself lists them, so the strip the
        // user meets on the home screen reads in the same order as the list they just filled in.
        new()
        {
            Id = "Notes",
            Title = AppResources.Notes,
            Group = OnboardingModuleGroup.Section,
            QuickMenuId = "Notes"
        },
        new()
        {
            Id = "Reading",
            Title = AppResources.ReadingTrackerTitle,
            Group = OnboardingModuleGroup.Section,
            QuickMenuId = "Reading"
        },
        new()
        {
            Id = "Moments",
            Title = AppResources.HappyMomentsTitle,
            Group = OnboardingModuleGroup.Section,
            QuickMenuId = "Moments"
        },
        new()
        {
            Id = "Deeds",
            Title = AppResources.GoodDeedsTitle,
            Group = OnboardingModuleGroup.Section,
            QuickMenuId = "Deeds"
        },
        new()
        {
            Id = "Wishlist",
            Title = AppResources.WishlistTitle,
            Group = OnboardingModuleGroup.Section,
            QuickMenuId = "Wishlist"
        },
        new()
        {
            Id = "Finance",
            Title = AppResources.FinanceTitle,
            Group = OnboardingModuleGroup.Section,
            QuickMenuId = "Finance"
        }
    };

    /// <inheritdoc />
    public void Apply(UserProfile profile, IReadOnlyList<OnboardingModule> modules)
    {
        foreach (var module in modules)
        {
            module.SetEnabled?.Invoke(profile, module.IsSelected);
        }

        profile.QuickMenuOrder = BuildQuickMenuOrder(modules);
    }

    /// <summary>
    /// Chosen tiles first, everything else after, in catalogue order.
    /// </summary>
    /// <remarks>
    /// Unchecking a tile moves it to the end of the strip rather than removing it: the quick menu has
    /// no hidden state, only an order (<see cref="QuickMenuViewModel"/> appends whatever the saved
    /// order never mentioned). Writing the full list rather than just the chosen ids says that out
    /// loud instead of leaning on that append.
    /// </remarks>
    private List<string> BuildQuickMenuOrder(IReadOnlyList<OnboardingModule> modules)
    {
        var chosen = modules
            .Where(module => module.IsSelected && !string.IsNullOrEmpty(module.QuickMenuId))
            .Select(module => module.QuickMenuId!)
            .ToList();

        var rest = _menuConfigurationService.GetDefaultMenuItems()
            .Select(item => item.Id)
            .Where(id => !chosen.Contains(id));

        return chosen.Concat(rest).ToList();
    }
}
