using System;
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
    [Fact]
    public async Task Complete_WithReminderEnabled_SavesProfileAndSchedules()
    {
        var profile = new UserProfile();
        var profileMock = new Mock<IProfileService>();
        profileMock.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(profile);
        var notifMock = new Mock<INotificationService>();
        notifMock.Setup(n => n.RequestPermissionsAsync()).ReturnsAsync(true);

        var vm = new OnboardingViewModel(profileMock.Object, notifMock.Object)
        {
            IsDailyReminderEnabled = true,
            DailyReminderTime = new TimeSpan(9, 30, 0)
        };
        var completed = false;
        vm.Completed = () => completed = true;

        await vm.CompleteCommand.ExecuteAsync(null);

        profileMock.Verify(s => s.SaveUserProfileAsync(
            It.Is<UserProfile>(p => p.IsDailyReminderEnabled && p.DailyReminderTime == new TimeSpan(9, 30, 0))), Times.Once);
        notifMock.Verify(n => n.ScheduleDailyJournalReminder(new TimeSpan(9, 30, 0)), Times.Once);
        completed.Should().BeTrue();
    }

    [Fact]
    public async Task Complete_WithReminderDisabled_CancelsAndDoesNotSchedule()
    {
        var profileMock = new Mock<IProfileService>();
        profileMock.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(new UserProfile());
        var notifMock = new Mock<INotificationService>();

        var vm = new OnboardingViewModel(profileMock.Object, notifMock.Object)
        {
            IsDailyReminderEnabled = false
        };
        var completed = false;
        vm.Completed = () => completed = true;

        await vm.CompleteCommand.ExecuteAsync(null);

        notifMock.Verify(n => n.ScheduleDailyJournalReminder(It.IsAny<TimeSpan>()), Times.Never);
        notifMock.Verify(n => n.CancelDailyJournalReminder(), Times.Once);
        completed.Should().BeTrue();
    }
}
