using System;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class OnboardingViewModelTests
{
    private readonly Mock<IProfileService> _profile = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly UserProfile _saved = new();

    public OnboardingViewModelTests()
    {
        _profile.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(_saved);
        _notifications.Setup(n => n.RequestPermissionsAsync()).ReturnsAsync(true);
    }

    /// <summary>
    /// The real module service, because half of what onboarding does is write the profile through it;
    /// a mock here would leave that wiring untested in both directions.
    /// </summary>
    private OnboardingViewModel CreateViewModel() =>
        new(_profile.Object, _notifications.Object, new OnboardingModuleService(new MenuConfigurationService()));

    [Fact]
    public async Task Complete_WithReminderEnabled_SavesProfileAndSchedules()
    {
        var vm = CreateViewModel();
        vm.IsDailyReminderEnabled = true;
        vm.DailyReminderTime = new TimeSpan(9, 30, 0);
        var completed = false;
        vm.Completed = () => completed = true;

        await vm.CompleteCommand.ExecuteAsync(null);

        _profile.Verify(s => s.SaveUserProfileAsync(
            It.Is<UserProfile>(p => p.IsDailyReminderEnabled && p.DailyReminderTime == new TimeSpan(9, 30, 0))), Times.Once);
        _notifications.Verify(n => n.ScheduleDailyJournalReminder(new TimeSpan(9, 30, 0)), Times.Once);
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task Complete_WithReminderDisabled_CancelsAndDoesNotSchedule()
    {
        var vm = CreateViewModel();
        vm.IsDailyReminderEnabled = false;
        var completed = false;
        vm.Completed = () => completed = true;

        await vm.CompleteCommand.ExecuteAsync(null);

        _notifications.Verify(n => n.ScheduleDailyJournalReminder(It.IsAny<TimeSpan>()), Times.Never);
        _notifications.Verify(n => n.CancelDailyJournalReminder(), Times.Once);
        completed.Should().BeTrue();
    }

    [Fact]
    public void Starts_OnTheFirstStep_WithNoWayBack()
    {
        var vm = CreateViewModel();

        vm.CurrentStep.Should().Be(OnboardingViewModel.WelcomeStep);
        vm.IsWelcomeStep.Should().BeTrue();
        vm.CanGoBack.Should().BeFalse();
        vm.CanGoNext.Should().BeTrue();
        vm.CanSkip.Should().BeTrue();
        vm.BackCommand.CanExecute(null).Should().BeFalse();
        vm.StepDots.Should().HaveCount(OnboardingViewModel.StepCount);
        vm.StepDots.Count(d => d.IsActive).Should().Be(1);
        vm.StepDots[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public void Next_WalksToTheLastStepAndStops()
    {
        var vm = CreateViewModel();

        for (var i = 0; i < OnboardingViewModel.StepCount - 1; i++)
        {
            vm.NextCommand.CanExecute(null).Should().BeTrue();
            vm.NextCommand.Execute(null);
        }

        vm.CurrentStep.Should().Be(OnboardingViewModel.ReminderStep);
        vm.IsReminderStep.Should().BeTrue();
        vm.CanGoNext.Should().BeFalse();
        vm.NextCommand.CanExecute(null).Should().BeFalse();

        // Skip disappears exactly where the finishing button appears, so there are never two ways out.
        vm.CanSkip.Should().BeFalse();
    }

    [Fact]
    public void Back_ReturnsToThePreviousStepAndMovesTheDot()
    {
        var vm = CreateViewModel();
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        vm.IsInsightsStep.Should().BeTrue();
        vm.StepDots[OnboardingViewModel.InsightsStep].IsActive.Should().BeTrue();

        vm.BackCommand.Execute(null);

        vm.CurrentStep.Should().Be(OnboardingViewModel.JournalStep);
        vm.IsJournalStep.Should().BeTrue();
        vm.StepDots[OnboardingViewModel.JournalStep].IsActive.Should().BeTrue();
        vm.StepDots.Count(d => d.IsActive).Should().Be(1);
    }

    [Fact]
    public void Modules_AreSplitIntoTheTwoGroups_AndAllStartOn()
    {
        var vm = CreateViewModel();

        vm.DailyEntryModules.Should().NotBeEmpty();
        vm.SectionModules.Should().NotBeEmpty();
        vm.DailyEntryModules.Should().OnlyContain(m => m.Group == OnboardingModuleGroup.DailyEntry);
        vm.SectionModules.Should().OnlyContain(m => m.Group == OnboardingModuleGroup.Section);
        vm.DailyEntryModules.Concat(vm.SectionModules).Should().OnlyContain(m => m.IsSelected);
    }

    [Fact]
    public async Task Complete_WritesTheModuleChoicesIntoTheProfile()
    {
        var vm = CreateViewModel();
        vm.DailyEntryModules.Single(m => m.Id == "Food").IsSelected = false;
        vm.SectionModules.Single(m => m.Id == "Reading").IsSelected = false;

        await vm.CompleteCommand.ExecuteAsync(null);

        _saved.IsFoodBlockVisible.Should().BeFalse();
        _saved.IsMoodBlockVisible.Should().BeTrue();
        _saved.QuickMenuOrder.Should().NotBeNull();
        _saved.QuickMenuOrder!.Should().Contain("Reading", "an unchecked tile is demoted, not dropped");
        _saved.QuickMenuOrder.IndexOf("Reading").Should().BeGreaterThan(_saved.QuickMenuOrder.IndexOf("Notes"));
    }

    [Fact]
    public async Task Skip_AsksForNothing_EvenWithTheReminderSwitchStillOn()
    {
        var vm = CreateViewModel();
        vm.IsDailyReminderEnabled = true;
        var completed = false;
        vm.Completed = () => completed = true;

        await vm.SkipCommand.ExecuteAsync(null);

        // The tap that says "leave me alone" must not be the tap that raises a permission prompt.
        _notifications.Verify(n => n.RequestPermissionsAsync(), Times.Never);
        _notifications.Verify(n => n.ScheduleDailyJournalReminder(It.IsAny<TimeSpan>()), Times.Never);
        _saved.IsDailyReminderEnabled.Should().BeFalse();
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task Skip_KeepsTheChoicesAlreadyMade()
    {
        var vm = CreateViewModel();
        vm.DailyEntryModules.Single(m => m.Id == "Sleep").IsSelected = false;

        await vm.SkipCommand.ExecuteAsync(null);

        _saved.IsSleepBlockVisible.Should().BeFalse();
        _profile.Verify(s => s.SaveUserProfileAsync(It.IsAny<UserProfile>()), Times.Once);
    }
}
