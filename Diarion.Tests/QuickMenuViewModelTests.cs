using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The quick menu's tiles and the saved order they appear in.
/// </summary>
public class QuickMenuViewModelTests
{
    private readonly Mock<IProfileService> _profiles = new();
    private readonly Mock<INavigationService> _navigation = new();
    private readonly UserProfile _profile = new();

    public QuickMenuViewModelTests()
    {
        _profiles.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(_profile);
        _profiles.Setup(p => p.SaveUserProfileAsync(It.IsAny<UserProfile>())).Returns(Task.CompletedTask);
    }

    private QuickMenuViewModel Build()
    {
        // The real catalogue, not a stub: the id the filter looks for has to be the id the
        // catalogue actually ships, and a stub would let those two drift apart unnoticed.
        var viewModel = new QuickMenuViewModel(
            new MenuConfigurationService(),
            _profiles.Object,
            _navigation.Object);

        viewModel.Initialize();
        return viewModel;
    }

    private static IEnumerable<string> Ids(QuickMenuViewModel viewModel) =>
        viewModel.QuickMenuItems.Select(i => i.Id);

    [Fact]
    public void EveryTileHasACommand()
    {
        var viewModel = Build();

        viewModel.QuickMenuItems.Should().OnlyContain(i => i.Command != null);
    }

    [Fact]
    public void TheSearchTileIsOfferedAndLeadsToTheSearchScreen()
    {
        // The tile is the only way into the search screen — no other page navigates to the route —
        // so this is the test that the diary can be searched at all. Keyword search needs no model;
        // the tile was retired with the generative model by mistake of association, not of fact.
        MenuConfigurationService.SearchOffered.Should().BeTrue();

        var viewModel = Build();
        Ids(viewModel).Should().Contain(MenuConfigurationService.SearchId);

        var search = viewModel.QuickMenuItems.Single(i => i.Id == MenuConfigurationService.SearchId);
        search.Command.Should().NotBeNull();
        search.Command!.Execute(null);
        _navigation.Verify(n => n.NavigateToAsync("Search"), Times.Once);
    }

    [Fact]
    public async Task ANewTileLandsAtTheEndOfAnOrderThatPredatesIt()
    {
        // Saved orders can predate tiles an update adds. Skipping unknown ids and appending the
        // leftovers is what keeps such an order usable across an update.
        _profile.QuickMenuOrder = ["Finance", "Notes"];
        var viewModel = Build();

        await viewModel.RefreshAsync();

        Ids(viewModel).Take(2).Should().Equal("Finance", "Notes");
        Ids(viewModel).Should().HaveCountGreaterThan(2, "the tiles the old order never named follow it");
    }

    [Fact]
    public async Task AnOrderNamingATileThatIsGone_DoesNotLeaveAGap()
    {
        // Orders saved before 2026-10-21 can still name the chat tile, which went with the local AI.
        // An id the catalogue no longer has is skipped, not drawn as an empty slot.
        _profile.QuickMenuOrder = ["AiChat", "Notes"];
        var viewModel = Build();

        await viewModel.RefreshAsync();

        Ids(viewModel).First().Should().Be("Notes");
        viewModel.QuickMenuItems.Should().NotContainNulls();
    }

    [Fact]
    public async Task AnOrderWrittenWhileAway_IsPickedUpOnTheWayBack()
    {
        // Onboarding's module picker writes the order and is then dismissed onto the home screen.
        // Without this the strip keeps the order it read at startup until the app is restarted, and
        // the picker looks like it did nothing.
        var viewModel = Build();
        await viewModel.RefreshAsync();

        _profile.QuickMenuOrder = ["Finance", "Notes"];
        await viewModel.RefreshAsync();

        Ids(viewModel).Take(2).Should().Equal("Finance", "Notes");
    }

    [Fact]
    public async Task NothingChanged_TheStripIsLeftAlone()
    {
        // Rebuild clears and refills the collection, so running it on every return to the home
        // screen would flicker the strip for nothing.
        var viewModel = Build();
        await viewModel.RefreshAsync();

        var changes = 0;
        viewModel.QuickMenuItems.CollectionChanged += (_, _) => changes++;
        await viewModel.RefreshAsync();

        changes.Should().Be(0);
    }

}
