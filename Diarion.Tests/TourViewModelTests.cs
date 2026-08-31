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
/// The guides, tested without a screen between them — which is the point of keeping the geometry
/// out of the view model.
/// </summary>
public class TourViewModelTests
{
    private readonly Mock<IProfileService> _profileService = new();
    private readonly UserProfile _profile = new();

    public TourViewModelTests()
    {
        _profileService.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(_profile);
    }

    private TourViewModel CreateViewModel() => new(_profileService.Object, new TourService());

    [Fact]
    public async Task Start_OnAFreshProfile_OpensOnTheFirstStep()
    {
        var vm = CreateViewModel();

        var started = await vm.StartAsync(TourId.Home);

        started.Should().BeTrue();
        vm.IsActive.Should().BeTrue();
        vm.CurrentStep!.Id.Should().Be(TourStepId.QuickMenu);
        vm.Title.Should().NotBeEmpty();
        vm.Body.Should().NotBeEmpty();
        vm.IsLastStep.Should().BeFalse();
        vm.HasMoreSteps.Should().BeTrue();
        vm.StepDots.Should().HaveCount(4);
        vm.StepDots.Count(d => d.IsActive).Should().Be(1);
        vm.StepDots[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Next_WalksTheScreenTopToBottom_ThenFinishes()
    {
        var vm = CreateViewModel();
        await vm.StartAsync(TourId.Home);

        await vm.NextCommand.ExecuteAsync(null);
        vm.CurrentStep!.Id.Should().Be(TourStepId.DayStrip);
        vm.StepDots[1].IsActive.Should().BeTrue();
        vm.StepDots.Count(d => d.IsActive).Should().Be(1);

        await vm.NextCommand.ExecuteAsync(null);
        vm.CurrentStep!.Id.Should().Be(TourStepId.Tabs);

        await vm.NextCommand.ExecuteAsync(null);
        vm.CurrentStep!.Id.Should().Be(TourStepId.DiaryBlocks);
        vm.IsLastStep.Should().BeTrue();
        vm.HasMoreSteps.Should().BeFalse();

        // One more tap on the last step ends it rather than doing nothing — the whole overlay is a
        // tap target, so "nothing happens" would read as a stuck screen.
        await vm.NextCommand.ExecuteAsync(null);
        vm.IsActive.Should().BeFalse();
        vm.CurrentStep.Should().BeNull();
    }

    [Fact]
    public async Task Finish_RecordsTheVersion_SoTheGuideDoesNotRunAgain()
    {
        var vm = CreateViewModel();
        await vm.StartAsync(TourId.Home);

        await vm.FinishCommand.ExecuteAsync(null);

        _profile.TourVersionSeen.Should().Be(TourService.CurrentVersion);
        _profileService.Verify(s => s.SaveUserProfileAsync(_profile), Times.Once);

        (await vm.StartAsync(TourId.Home)).Should().BeFalse();
        vm.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Skip_CountsAsSeen()
    {
        var vm = CreateViewModel();
        await vm.StartAsync(TourId.Home);

        await vm.SkipCommand.ExecuteAsync(null);

        // Dismissing is an answer. Asking again tomorrow would be arguing with it.
        vm.IsActive.Should().BeFalse();
        _profile.TourVersionSeen.Should().Be(TourService.CurrentVersion);
    }

    [Fact]
    public async Task Start_Forced_RunsAgainForSomeoneWhoAlreadySawIt()
    {
        var vm = CreateViewModel();
        _profile.TourVersionSeen = TourService.CurrentVersion;

        (await vm.StartAsync(TourId.Home)).Should().BeFalse();
        (await vm.StartAsync(TourId.Home, force: true)).Should().BeTrue();
        vm.CurrentStep!.Id.Should().Be(TourStepId.QuickMenu);
    }

    [Fact]
    public async Task Start_WhenEveryDiaryBlockIsOff_SkipsThatStep()
    {
        TurnOffEveryDiaryBlock();
        var vm = CreateViewModel();

        await vm.StartAsync(TourId.Home);

        vm.StepDots.Should().HaveCount(3);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        vm.CurrentStep!.Id.Should().Be(TourStepId.Tabs);
        vm.IsLastStep.Should().BeTrue("a spotlight around an empty diary tab points at nothing");
    }

    [Fact]
    public async Task Start_TwiceInARow_DoesNotRestartTheRunningTour()
    {
        var vm = CreateViewModel();
        await vm.StartAsync(TourId.Home);
        await vm.NextCommand.ExecuteAsync(null);

        (await vm.StartAsync(TourId.Home)).Should().BeFalse();
        vm.CurrentStep!.Id.Should().Be(TourStepId.DayStrip);
    }

    [Fact]
    public async Task Finish_RecordsTheGuideThatWasRunning_AndNoOther()
    {
        var vm = CreateViewModel();
        await vm.StartAsync(TourId.Statistics);

        vm.CurrentStep!.Id.Should().Be(TourStepId.StatsPeriod);

        await vm.FinishCommand.ExecuteAsync(null);

        _profile.StatsTourVersionSeen.Should().Be(TourService.CurrentVersion);
        _profile.TourVersionSeen.Should().Be(0);
        _profile.SettingsTourVersionSeen.Should().Be(0);
    }

    [Fact]
    public async Task PrimaryButton_RenamesItselfOnTheLastStep_RatherThanBeingASecondButton()
    {
        // Two buttons sharing one cell and swapping on IsVisible skipped a step on Windows: the tap
        // that hid "next" landed on the "done" that appeared under the pointer. One button, one command.
        var vm = CreateViewModel();
        await vm.StartAsync(TourId.Statistics);

        var next = vm.PrimaryButtonText;
        next.Should().NotBeEmpty();

        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        vm.PrimaryButtonText.Should().Be(next, "three of four steps still have somewhere to go");

        await vm.NextCommand.ExecuteAsync(null);
        vm.IsLastStep.Should().BeTrue();
        vm.PrimaryButtonText.Should().NotBe(next);
    }

    [Fact]
    public async Task Start_OfferedPerScreen_SoSeeingOneDoesNotSpendTheOthers()
    {
        var vm = CreateViewModel();

        await vm.StartAsync(TourId.Settings);
        await vm.SkipCommand.ExecuteAsync(null);

        (await vm.StartAsync(TourId.Settings)).Should().BeFalse();
        (await vm.StartAsync(TourId.Home)).Should().BeTrue();
    }

    private void TurnOffEveryDiaryBlock()
    {
        _profile.IsMoodBlockVisible = false;
        _profile.IsSleepBlockVisible = false;
        _profile.IsHealthBlockVisible = false;
        _profile.IsFoodBlockVisible = false;
        _profile.IsHabitsBlockVisible = false;
        _profile.IsGuidedPromptBlockVisible = false;
        _profile.IsReflectionBlockVisible = false;
    }
}
