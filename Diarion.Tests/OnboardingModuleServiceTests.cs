using System.Linq;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The real <see cref="MenuConfigurationService"/> rather than a mock: the point of most of these is
/// that the order onboarding writes matches the catalogue the quick menu actually renders, and a
/// stubbed catalogue would make every one of them pass by construction.
/// </summary>
public class OnboardingModuleServiceTests
{
    private static OnboardingModuleService CreateService() => new(new MenuConfigurationService());

    [Fact]
    public void GetModules_StartsWithEverythingSelected()
    {
        var modules = CreateService().GetModules();

        modules.Should().NotBeEmpty();
        modules.Should().OnlyContain(m => m.IsSelected);
        modules.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.Title));
        modules.Select(m => m.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetModules_DoesNotOfferCycleOrAiChat()
    {
        // Both are deliberate omissions, not oversights - see the remarks on GetModules. If either
        // is ever added, that is a product decision and this test is the place it gets noticed.
        var ids = CreateService().GetModules().Select(m => m.Id);

        ids.Should().NotContain("Cycle");
        ids.Should().NotContain(MenuConfigurationService.AiChatId);
    }

    [Fact]
    public void GetModules_IsBuiltFresh_SoSelectionsDoNotLeakBetweenCalls()
    {
        var service = CreateService();

        service.GetModules()[0].IsSelected = false;

        service.GetModules()[0].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void Apply_UncheckedModule_HidesItsBlock()
    {
        var service = CreateService();
        var modules = service.GetModules();
        var profile = new UserProfile();

        modules.Single(m => m.Id == "Food").IsSelected = false;
        modules.Single(m => m.Id == "Reflection").IsSelected = false;

        service.Apply(profile, modules);

        profile.IsFoodBlockVisible.Should().BeFalse();
        profile.IsReflectionBlockVisible.Should().BeFalse();
        profile.IsMoodBlockVisible.Should().BeTrue();
        profile.IsSleepBlockVisible.Should().BeTrue();
        profile.IsHealthBlockVisible.Should().BeTrue();
        profile.IsGuidedPromptBlockVisible.Should().BeTrue();
        profile.IsHabitsBlockVisible.Should().BeTrue();
    }

    [Fact]
    public void Apply_HabitsUnchecked_HidesTheBlockAndDemotesTheTile()
    {
        // Habits is the only module that owns both halves, so it is the only one where the two
        // writes can disagree.
        var service = CreateService();
        var modules = service.GetModules();
        var profile = new UserProfile();

        modules.Single(m => m.Id == "Habits").IsSelected = false;

        service.Apply(profile, modules);

        profile.IsHabitsBlockVisible.Should().BeFalse();

        // The tile is not removed, only pushed behind every tile that was kept.
        profile.QuickMenuOrder!.Take(6).Should().Equal("Notes", "Reading", "Moments", "Deeds", "Wishlist", "Finance");
        profile.QuickMenuOrder.Should().Contain("Habits");
    }

    [Fact]
    public void Apply_PutsChosenTilesFirstAndKeepsEveryOtherOne()
    {
        var service = CreateService();
        var modules = service.GetModules();
        var profile = new UserProfile();

        foreach (var module in modules.Where(m => m.Id is not ("Notes" or "Finance")))
        {
            module.IsSelected = false;
        }

        service.Apply(profile, modules);

        profile.QuickMenuOrder.Should().NotBeNull();
        profile.QuickMenuOrder!.Take(2).Should().Equal("Notes", "Finance");

        // Nothing is dropped: the quick menu has no hidden state, only an order.
        var everyTile = new MenuConfigurationService().GetDefaultMenuItems().Select(i => i.Id);
        profile.QuickMenuOrder.Should().BeEquivalentTo(everyTile);
    }

    [Fact]
    public void Apply_WithEverythingSelected_LeavesTheCatalogueOrderIntact()
    {
        var service = CreateService();
        var profile = new UserProfile();

        service.Apply(profile, service.GetModules());

        // Habits sits in the daily-entry group and so is listed before the tile-only modules; the
        // rest follow the menu catalogue.
        profile.QuickMenuOrder!.Should().Equal(
            "Habits", "Notes", "Reading", "Moments", "Deeds", "Wishlist", "Finance",
            MenuConfigurationService.AiChatId);
    }
}
