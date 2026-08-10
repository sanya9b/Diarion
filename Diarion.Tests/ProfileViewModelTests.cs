using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
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
    public void AiTab_IsHiddenWhileTheLocalModelsAreRetired()
    {
        var vm = Create(new Mock<IProfileService>(), new Mock<INotificationService>());

        vm.IsAiTabAvailable.Should().BeFalse();
    }

    [Fact]
    public void SelectingTheAiTabByIndex_DoesNotShowIt()
    {
        // The chip is hidden, but SelectTab takes a string and nothing stops a stale binding or a
        // future fifth tab from sending "3". The section must stay closed on the strength of the
        // flag alone.
        var vm = Create(new Mock<IProfileService>(), new Mock<INotificationService>());

        vm.SelectTabCommand.Execute("3");

        vm.IsAiTab.Should().BeFalse();
        vm.IsProfileTab.Should().BeFalse("the tap was not silently redirected — it simply shows nothing");
    }

    [Fact]
    public async Task LoadProfile_DoesNotWakeTheAiSection_WhileTheTabIsHidden()
    {
        // Load() stats the model directory and subscribes each row to download progress. None of
        // that leads anywhere now, and it runs on every visit to settings.
        var profileMock = new Mock<IProfileService>();
        profileMock.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile());

        var vm = Create(profileMock, new Mock<INotificationService>());
        await vm.LoadProfileAsync();

        vm.Ai.Models.Should().BeEmpty();
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
