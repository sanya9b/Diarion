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

        await viewModel.RefreshAvailabilityAsync();

        Ids(viewModel).Should().NotContain(MenuConfigurationService.AiChatId);
        Ids(viewModel).Should().Contain("Search", "the rest of the menu is unaffected");
    }

    [Fact]
    public async Task AiSwitchedOff_TheChatTileGoesAway()
    {
        var viewModel = Build();
        await viewModel.RefreshAvailabilityAsync();
        Ids(viewModel).Should().Contain(MenuConfigurationService.AiChatId, "otherwise the test proves nothing");

        _availability.CanEmbed = false;
        await viewModel.RefreshAvailabilityAsync();

        Ids(viewModel).Should().NotContain(MenuConfigurationService.AiChatId);
    }

    [Fact]
    public async Task ModelInstalledWhileAway_TheTileAppearsOnTheWayBack()
    {
        // The model is downloaded in settings. Nothing else brings the user past this code.
        _availability.CanGenerate = false;
        var viewModel = Build();
        await viewModel.RefreshAvailabilityAsync();

        _availability.CanGenerate = true;
        await viewModel.RefreshAvailabilityAsync();

        Ids(viewModel).Should().Contain(MenuConfigurationService.AiChatId);
    }

    [Fact]
    public async Task TheChatTileHasACommand()
    {
        // A tile with no command is a button that does nothing when tapped — and the switch that
        // assigns commands is keyed by id, so a renamed id would fail exactly this way.
        var viewModel = Build();
        await viewModel.RefreshAvailabilityAsync();

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
    public async Task ANewTileLandsAtTheEndOfAnOrderThatPredatesIt()
    {
        // Saved orders in the wild were written before chat existed. Skipping unknown ids and
        // appending the leftovers is what keeps such an order usable across an update.
        _profile.QuickMenuOrder = ["Finance", "Search"];
        var viewModel = Build();

        await viewModel.RefreshAvailabilityAsync();

        Ids(viewModel).Take(2).Should().Equal("Finance", "Search");
        Ids(viewModel).Last().Should().Be(MenuConfigurationService.AiChatId);
    }

    [Fact]
    public async Task AnOrderNamingATileThatIsHidden_DoesNotLeaveAGap()
    {
        _profile.QuickMenuOrder = [MenuConfigurationService.AiChatId, "Search"];
        _availability.CanGenerate = false;
        var viewModel = Build();

        await viewModel.RefreshAvailabilityAsync();

        Ids(viewModel).First().Should().Be("Search");
        viewModel.QuickMenuItems.Should().NotContainNulls();
    }

    [Fact]
    public async Task ReorderingWhileChatIsHidden_DoesNotBanishItForever()
    {
        // Dragging saves the visible ids, so a hidden chat drops out of the stored order. It has to
        // come back when the model does — as a new tile at the end, which is the honest place for it.
        _profile.QuickMenuOrder = ["Search", "Notes"];
        _availability.CanGenerate = false;
        var viewModel = Build();
        await viewModel.RefreshAvailabilityAsync();

        viewModel.DragMenuStartingCommand.Execute(viewModel.QuickMenuItems[0]);
        await viewModel.ReorderMenuCommand.ExecuteAsync(viewModel.QuickMenuItems[1]);
        _profile.QuickMenuOrder.Should().NotContain(MenuConfigurationService.AiChatId);

        _availability.CanGenerate = true;
        await viewModel.RefreshAvailabilityAsync();

        Ids(viewModel).Should().Contain(MenuConfigurationService.AiChatId);
    }
}
