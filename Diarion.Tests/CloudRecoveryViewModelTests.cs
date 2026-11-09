using System;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Services;
using Diarion.Services.Recovery;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>The settings card (spec 21): the switch is the consent, and turning it off asks about the copy.</summary>
public class CloudRecoveryViewModelTests
{
    private readonly Mock<ICloudRecoveryService> _service = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly CloudRecoverySettings _settings = new();

    public CloudRecoveryViewModelTests()
    {
        _service.SetupGet(s => s.IsSupported).Returns(true);
        _service.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _service.SetupGet(s => s.Settings).Returns(_settings);
        _service.Setup(s => s.EnableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CloudRecoveryOutcome.Success);
        _service.Setup(s => s.CopyNowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CloudRecoveryOutcome.Success);
        _service.Setup(s => s.DisableAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private CloudRecoveryViewModel Build() => new(_service.Object, _dialogs.Object);

    [Fact]
    public async Task Load_ReadsTheStoredDecision_WithoutTreatingItAsAToggle()
    {
        _settings.Decision = CloudRecoveryDecision.Enabled;
        _settings.LastCopyUtc = new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
        var vm = Build();

        await vm.LoadAsync();

        vm.IsEnabled.Should().BeTrue();
        vm.IsAvailable.Should().BeTrue();
        vm.StatusText.Should().Contain(CloudRecoveryViewModel.CopyDate(_settings.LastCopyUtc));
        _service.Verify(s => s.EnableAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Load_WhenSignedOut_ShowsTheUnavailableNote()
    {
        _service.Setup(s => s.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var vm = Build();

        await vm.LoadAsync();

        vm.IsUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task SwitchingOn_IsTheConsent_AndCopiesAtOnce()
    {
        var vm = Build();
        await vm.LoadAsync();

        vm.IsEnabled = true;
        await Task.Delay(50);

        _service.Verify(s => s.EnableAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SwitchingOn_WhenTheCopyFails_TellsTheUser()
    {
        _service.Setup(s => s.EnableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CloudRecoveryOutcome.Unavailable);
        var vm = Build();
        await vm.LoadAsync();

        vm.IsEnabled = true;
        await Task.Delay(50);

        _dialogs.Verify(d => d.ShowAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SwitchingOff_WithACopyInTheCloud_AsksWhetherToDeleteIt()
    {
        _settings.Decision = CloudRecoveryDecision.Enabled;
        _settings.LastCopyUtc = DateTime.UtcNow;
        _dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        var vm = Build();
        await vm.LoadAsync();

        vm.IsEnabled = false;
        await Task.Delay(50);

        _service.Verify(s => s.DisableAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SwitchingOff_WithNoCopyYet_DoesNotAsk()
    {
        _settings.Decision = CloudRecoveryDecision.Enabled;
        _settings.LastCopyUtc = null;
        var vm = Build();
        await vm.LoadAsync();

        vm.IsEnabled = false;
        await Task.Delay(50);

        _dialogs.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _service.Verify(s => s.DisableAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Restore_AsksFirst_AndDoesNothingOnNo()
    {
        _service.Setup(s => s.FindCopyAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecoveryCopyInfo(DateTime.UtcNow, 1000, true));
        _dialogs.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        var vm = Build();

        await vm.RestoreCommand.ExecuteAsync(null);

        _service.Verify(s => s.RestoreAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Restore_WithNoCopy_SaysSoWithoutAsking()
    {
        _service.Setup(s => s.FindCopyAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecoveryCopyInfo?)null);
        var vm = Build();

        await vm.RestoreCommand.ExecuteAsync(null);

        _dialogs.Verify(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _dialogs.Verify(d => d.ShowAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Unsupported_HidesEverything()
    {
        _service.SetupGet(s => s.IsSupported).Returns(false);
        var vm = Build();

        vm.IsSupported.Should().BeFalse();
        vm.IsUnsupported.Should().BeTrue();
        vm.IsUnavailable.Should().BeFalse("there is nothing to be unavailable");
    }
}
