using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class ProfileViewModelTests
{
    private static ProfileViewModel Create(Mock<IProfileService> profile, Mock<INotificationService> notif)
    {
        return new ProfileViewModel(
            profile.Object,
            new Mock<IBackupService>().Object,
            new Mock<IAppLockService>().Object,
            new Mock<IBiometricService>().Object,
            new Mock<IDialogService>().Object,
            notif.Object,
            new Mock<IExportService>().Object,
            new Mock<INavigationService>().Object,
            new Mock<Diarion.Diagnostics.ICrashReporter>().Object,
            new Mock<IShareService>().Object);
    }

    [Fact]
    public async Task SaveProfile_ReminderEnabled_SchedulesDailyReminder()
    {
        var profileMock = new Mock<IProfileService>();
        var notifMock = new Mock<INotificationService>();
        notifMock.Setup(n => n.RequestPermissionsAsync()).ReturnsAsync(true);

        var vm = Create(profileMock, notifMock);
        vm.Profile.IsDailyReminderEnabled = true;
        vm.Profile.DailyReminderTime = new TimeSpan(7, 0, 0);

        await vm.SaveProfileCommand.ExecuteAsync(null);

        notifMock.Verify(n => n.ScheduleDailyJournalReminder(new TimeSpan(7, 0, 0)), Times.Once);
        notifMock.Verify(n => n.CancelDailyJournalReminder(), Times.Never);
    }

    [Fact]
    public async Task SaveProfile_ReminderDisabled_CancelsDailyReminder()
    {
        var profileMock = new Mock<IProfileService>();
        var notifMock = new Mock<INotificationService>();

        var vm = Create(profileMock, notifMock);
        vm.Profile.IsDailyReminderEnabled = false;

        await vm.SaveProfileCommand.ExecuteAsync(null);

        notifMock.Verify(n => n.CancelDailyJournalReminder(), Times.Once);
        notifMock.Verify(n => n.ScheduleDailyJournalReminder(It.IsAny<TimeSpan>()), Times.Never);
    }
}
