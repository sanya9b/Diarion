using System;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai;
using Diarion.Services;
using Diarion.Services.Ai;
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
    public void AiTab_IsThereForAsLongAsAnythingLocalIsOffered()
    {
        var vm = Create(new Mock<IProfileService>(), new Mock<INotificationService>());

        vm.IsAiTabAvailable.Should().Be(OnDeviceAi.IsOffered);
        vm.IsAiTabAvailable.Should().BeTrue("the encoder still feeds themes and mood factors");
    }

    [Fact]
    public void SelectingTheAiTab_OpensIt()
    {
        // SelectTab takes a string, and IsAiTab weighs the index against the flag rather than
        // trusting it — so this is also the test that the flag is consulted at all.
        var vm = Create(new Mock<IProfileService>(), new Mock<INotificationService>());

        vm.SelectTabCommand.Execute("3");

        vm.IsAiTab.Should().BeTrue();
        vm.IsProfileTab.Should().BeFalse();
    }

    [Fact]
    public async Task LoadProfile_ListsTheEncoderAndNotTheGenerativeModel()
    {
        // Settings must not offer a 981 MB download for a model nothing is allowed to call. The row
        // is filtered where the list is built, so this is the test that the filter is wired at all.
        var profileMock = new Mock<IProfileService>();
        profileMock.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile());

        var vm = Create(profileMock, new Mock<INotificationService>());
        await vm.LoadProfileAsync();

        vm.Ai.Models.Should().NotBeEmpty();
        vm.Ai.Models.Should().OnlyContain(m => m.Descriptor.Kind == AiModelKind.Embedding);
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
