using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Backup;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The switch on the settings card. What is guarded: it only stays on when the whole conversation
/// — folder, passphrase, passphrase again — finishes, and the first copy follows at once.
/// </summary>
public class ScheduledBackupViewModelTests
{
    private readonly Mock<IScheduledBackupService> _service = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly ScheduledBackupSettings _settings = new();

    public ScheduledBackupViewModelTests()
    {
        _service.SetupGet(s => s.IsSupported).Returns(true);
        _service.SetupGet(s => s.Settings).Returns(_settings);
        _service.Setup(s => s.PickFolderAsync()).ReturnsAsync(new BackupFolderPick("tree://x", "iCloud Drive/Diarion"));
        _service.Setup(s => s.RunNowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ScheduledBackupOutcome.Success);
        _service.Setup(s => s.Enable(It.IsAny<BackupFolderPick>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback((BackupFolderPick f, string p, int d) =>
                {
                    _settings.IsEnabled = true;
                    _settings.FolderToken = f.Token;
                    _settings.FolderName = f.DisplayName;
                    _settings.IntervalDays = d;
                });
        _service.Setup(s => s.Disable()).Callback(() => _settings.IsEnabled = false);

        PassphraseAnswers("hunter2", "hunter2");
    }

    private void PassphraseAnswers(string first, string second)
    {
        var calls = 0;
        _dialogs.Setup(d => d.ShowPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => calls++ == 0 ? first : second);
    }

    private ScheduledBackupViewModel Create()
    {
        var vm = new ScheduledBackupViewModel(_service.Object, _dialogs.Object);
        vm.Load();
        return vm;
    }

    /// <summary>EnableAsync is fired from a property setter, so the test waits for its effect.</summary>
    private static async Task SettleAsync(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!done() && DateTime.UtcNow < deadline) await Task.Delay(10);
    }

    [Fact]
    public void Load_reflects_the_stored_settings_without_treating_them_as_edits()
    {
        _settings.IsEnabled = true;
        _settings.IntervalDays = 14;
        _settings.FolderName = "Google Drive/Diarion";
        _settings.LastRunUtc = new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc);
        _settings.LastOutcome = ScheduledBackupOutcome.Success;

        var vm = Create();

        vm.IsEnabled.Should().BeTrue();
        vm.SelectedInterval!.Days.Should().Be(14);
        vm.FolderText.Should().Contain("Google Drive/Diarion");
        vm.StatusText.Should().Contain(_settings.LastRunUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        _service.Verify(s => s.Enable(It.IsAny<BackupFolderPick>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _service.Verify(s => s.SetInterval(It.IsAny<int>()), Times.Never);
        _service.Verify(s => s.Disable(), Times.Never);
    }

    [Fact]
    public async Task Switching_on_picks_a_folder_asks_the_passphrase_twice_enables_and_makes_the_first_copy()
    {
        var vm = Create();
        vm.SelectedInterval = vm.Intervals[1]; // 3 days, chosen before the switch

        vm.IsEnabled = true;
        await SettleAsync(() => _settings.IsEnabled);

        _service.Verify(s => s.PickFolderAsync(), Times.Once);
        _dialogs.Verify(d => d.ShowPromptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        _service.Verify(s => s.Enable(It.Is<BackupFolderPick>(f => f.Token == "tree://x"), "hunter2", 3), Times.Once);
        _service.Verify(s => s.RunNowAsync(It.IsAny<CancellationToken>()), Times.Once);
        vm.IsEnabled.Should().BeTrue();
        vm.FolderText.Should().Contain("iCloud Drive/Diarion");
    }

    [Fact]
    public async Task Backing_out_of_the_folder_picker_puts_the_switch_back()
    {
        _service.Setup(s => s.PickFolderAsync()).ReturnsAsync((BackupFolderPick?)null);
        var vm = Create();

        vm.IsEnabled = true;
        await SettleAsync(() => !vm.IsEnabled);

        vm.IsEnabled.Should().BeFalse();
        _service.Verify(s => s.Enable(It.IsAny<BackupFolderPick>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _service.Verify(s => s.Disable(), Times.Never, "a switch that never went on has nothing to switch off");
    }

    [Fact]
    public async Task A_picker_that_throws_puts_the_switch_back_and_says_so()
    {
        // Enabling runs from a property setter, where nothing would ever observe an exception. On
        // Windows this was the FutureAccessList refusing an unpackaged app: the switch stayed on
        // with no folder behind it.
        _service.Setup(s => s.PickFolderAsync()).ThrowsAsync(new InvalidOperationException("no identity"));
        var vm = Create();

        vm.IsEnabled = true;
        await SettleAsync(() => !vm.IsEnabled);

        vm.IsEnabled.Should().BeFalse();
        _dialogs.Verify(d => d.ShowAlertAsync(It.IsAny<string>(), AppResources.AutoBackupOutcomeFailed, It.IsAny<string>()), Times.Once);
        _service.Verify(s => s.Enable(It.IsAny<BackupFolderPick>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Mismatched_passphrases_are_refused_and_the_switch_goes_back()
    {
        PassphraseAnswers("hunter2", "hunter3");
        var vm = Create();

        vm.IsEnabled = true;
        await SettleAsync(() => !vm.IsEnabled);

        vm.IsEnabled.Should().BeFalse();
        _dialogs.Verify(d => d.ShowAlertAsync(It.IsAny<string>(), AppResources.AutoBackupPassphraseMismatch, It.IsAny<string>()), Times.Once);
        _service.Verify(s => s.Enable(It.IsAny<BackupFolderPick>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task An_empty_passphrase_is_a_cancel()
    {
        PassphraseAnswers("", "");
        var vm = Create();

        vm.IsEnabled = true;
        await SettleAsync(() => !vm.IsEnabled);

        vm.IsEnabled.Should().BeFalse();
        _service.Verify(s => s.Enable(It.IsAny<BackupFolderPick>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Switching_off_disables()
    {
        _settings.IsEnabled = true;
        var vm = Create();

        vm.IsEnabled = false;

        _service.Verify(s => s.Disable(), Times.Once);
    }

    [Fact]
    public void Changing_the_interval_is_passed_on_but_loading_it_is_not()
    {
        _settings.IntervalDays = 7;
        var vm = Create();

        vm.SelectedInterval = vm.Intervals[0];

        _service.Verify(s => s.SetInterval(1), Times.Once);
    }

    [Fact]
    public async Task A_failed_run_now_is_said_out_loud()
    {
        _settings.IsEnabled = true;
        _settings.LastRunUtc = new DateTime(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc);
        _service.Setup(s => s.RunNowAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    _settings.LastOutcome = ScheduledBackupOutcome.FolderUnavailable;
                    return ScheduledBackupOutcome.FolderUnavailable;
                });
        var vm = Create();

        await vm.RunNowCommand.ExecuteAsync(null);

        _dialogs.Verify(d => d.ShowAlertAsync(It.IsAny<string>(), AppResources.AutoBackupOutcomeFolderUnavailable, It.IsAny<string>()), Times.Once);
        vm.StatusText.Should().StartWith(AppResources.AutoBackupOutcomeFolderUnavailable);
        vm.StatusText.Should().Contain(_settings.LastRunUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            "a failure is told together with the last good copy, which is how exposed the person is");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Changing_the_folder_keeps_everything_else()
    {
        _settings.IsEnabled = true;
        _settings.FolderName = "old";
        var vm = Create();
        _service.Setup(s => s.SetFolder(It.IsAny<BackupFolderPick>()))
                .Callback((BackupFolderPick f) => _settings.FolderName = f.DisplayName);

        await vm.ChangeFolderCommand.ExecuteAsync(null);

        _service.Verify(s => s.SetFolder(It.Is<BackupFolderPick>(f => f.DisplayName == "iCloud Drive/Diarion")), Times.Once);
        vm.FolderText.Should().Contain("iCloud Drive/Diarion");
        vm.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Status_says_never_until_the_first_copy_and_names_the_failure_afterwards()
    {
        var culture = CultureInfo.CurrentCulture;

        ScheduledBackupViewModel.StatusFor(new ScheduledBackupSettings(), culture)
            .Should().Be(AppResources.AutoBackupNeverRun);

        var failedFirstTime = new ScheduledBackupSettings { LastOutcome = ScheduledBackupOutcome.Failed };
        ScheduledBackupViewModel.StatusFor(failedFirstTime, culture)
            .Should().Be(AppResources.AutoBackupOutcomeFailed);
    }

    [Fact]
    public void Unsupported_platform_is_visible_to_the_card()
    {
        _service.SetupGet(s => s.IsSupported).Returns(false);

        var vm = Create();

        vm.IsSupported.Should().BeFalse();
        vm.IsUnsupported.Should().BeTrue();
    }
}
