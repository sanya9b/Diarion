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
/// The quick menu is a fixed row of tiles except for one: chat appears only when a generative model
/// is installed and AI is on. These tests cover that tile and its interaction with the saved order.
/// </summary>
public class QuickMenuViewModelTests
{
    private readonly Mock<IProfileService> _profiles = new();
    private readonly Mock<INavigationService> _navigation = new();
    private readonly FakeAiAvailability _availability = new();
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
            _navigation.Object,
            _availability);

        viewModel.Initialize();
        return viewModel;
    }

    private static IEnumerable<string> Ids(QuickMenuViewModel viewModel) =>
        viewModel.QuickMenuItems.Select(i => i.Id);

    [Fact]
    public async Task NoGenerativeModel_TheChatTileIsNotThere()
    {
        _availability.CanGenerate = false;
        var viewModel = Build();

        await viewModel.RefreshAsync();

        Ids(viewModel).Should().NotContain(MenuConfigurationService.AiChatId);
        Ids(viewModel).Should().Contain("Notes", "the rest of the menu is unaffected");
    }

    [Fact]
    public async Task AiSwitchedOff_TheChatTileGoesAway()
    {
        var viewModel = Build();
        await viewModel.RefreshAsync();
        Ids(viewModel).Should().Contain(MenuConfigurationService.AiChatId, "otherwise the test proves nothing");

        _availability.CanEmbed = false;
        await viewModel.RefreshAsync();

        Ids(viewModel).Should().NotContain(MenuConfigurationService.AiChatId);
    }

    [Fact]
    public async Task ModelInstalledWhileAway_TheTileAppearsOnTheWayBack()
    {
        // The model is downloaded in settings. Nothing else brings the user past this code.
        _availability.CanGenerate = false;
        var viewModel = Build();
        await viewModel.RefreshAsync();

        _availability.CanGenerate = true;
        await viewModel.RefreshAsync();

        Ids(viewModel).Should().Contain(MenuConfigurationService.AiChatId);
    }

    [Fact]
    public async Task TheChatTileHasACommand()
    {
        // A tile with no command is a button that does nothing when tapped — and the switch that
        // assigns commands is keyed by id, so a renamed id would fail exactly this way.
        var viewModel = Build();
        await viewModel.RefreshAsync();

        var chat = viewModel.QuickMenuItems.Single(i => i.Id == MenuConfigurationService.AiChatId);

        chat.Command.Should().NotBeNull();
        chat.Command!.Execute(null);
        _navigation.Verify(n => n.NavigateToAsync("AiChat"), Times.Once);
    }

    [Fact]
    public void EveryTileHasACommand()
    {
        var viewModel = Build();

        viewModel.QuickMenuItems.Should().OnlyContain(i => i.Command != null);
    }

    [Fact]
    public void TheSearchTileIsNotOffered()
    {
        // The tile is the only way into the search screen — no other page navigates to the route —
        // so a failure here means the retired screen is reachable again.
        MenuConfigurationService.SearchOffered.Should().BeFalse();

        Ids(Build()).Should().NotContain(MenuConfigurationService.SearchId);
    }

    [Fact]
    public async Task ANewTileLandsAtTheEndOfAnOrderThatPredatesIt()
    {
        // Saved orders in the wild were written before chat existed. Skipping unknown ids and
        // appending the leftovers is what keeps such an order usable across an update.
        _profile.QuickMenuOrder = ["Finance", "Notes"];
        var viewModel = Build();

        await viewModel.RefreshAsync();

        Ids(viewModel).Take(2).Should().Equal("Finance", "Notes");
        Ids(viewModel).Last().Should().Be(MenuConfigurationService.AiChatId);
    }

    [Fact]
    public async Task AnOrderNamingATileThatIsHidden_DoesNotLeaveAGap()
    {
        _profile.QuickMenuOrder = [MenuConfigurationService.AiChatId, "Notes"];
        _availability.CanGenerate = false;
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

    [Fact]
    public async Task ReorderingWhileChatIsHidden_DoesNotBanishItForever()
    {
        // Dragging saves the visible ids, so a hidden chat drops out of the stored order. It has to
        // come back when the model does — as a new tile at the end, which is the honest place for it.
        _profile.QuickMenuOrder = ["Notes", "Reading"];
        _availability.CanGenerate = false;
        var viewModel = Build();
        await viewModel.RefreshAsync();

        viewModel.DragMenuStartingCommand.Execute(viewModel.QuickMenuItems[0]);
        await viewModel.ReorderMenuCommand.ExecuteAsync(viewModel.QuickMenuItems[1]);
        _profile.QuickMenuOrder.Should().NotContain(MenuConfigurationService.AiChatId);

        _availability.CanGenerate = true;
        await viewModel.RefreshAsync();

        Ids(viewModel).Should().Contain(MenuConfigurationService.AiChatId);
    }
}
