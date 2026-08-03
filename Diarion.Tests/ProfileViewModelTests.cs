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
            new Mock<IShareService>().Object,
            CreateAiSection());
    }

    /// <summary>
    /// The AI tab is a section view model rather than more fields on ProfileViewModel. Its own
    /// behaviour is covered elsewhere; here it only has to exist and survive <c>Load</c>.
    /// </summary>
    private static AiSettingsViewModel CreateAiSection()
    {
        var dispatcher = new Mock<IDispatcherService>();
        dispatcher.Setup(d => d.InvokeOnMainThread(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        return new AiSettingsViewModel(
            new Mock<Diarion.Services.Ai.IModelDownloadService>().Object,
            new Mock<Diarion.Services.Ai.IDeviceCapabilityProbe>().Object,
            new Mock<Diarion.Services.Ai.IEmbeddingIndexService>().Object,
            new Mock<Diarion.Services.Ai.IVectorStore>().Object,
            new Mock<Diarion.Services.Ai.ITextEmbedder>().Object,
            new Mock<IDialogService>().Object,
            dispatcher.Object);
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
