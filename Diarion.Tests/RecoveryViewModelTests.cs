using System;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Resources.Localization;
using Diarion.Services.Recovery;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>The first-launch offer (spec 21, D4): restore on a tap, start fresh on the other, stay on a failure.</summary>
public class RecoveryViewModelTests
{
    private readonly Mock<ICloudRecoveryService> _service = new();

    private RecoveryViewModel Build()
    {
        var vm = new RecoveryViewModel(_service.Object);
        vm.Prepare(new RecoveryCopyInfo(new DateTime(2026, 11, 8, 18, 30, 0, DateTimeKind.Utc), 500_000, true));
        return vm;
    }

    [Fact]
    public void Prepare_NamesTheCopyByItsDate()
    {
        var vm = Build();
        vm.FoundText.Should().Contain(CloudRecoveryViewModel.CopyDate(new DateTime(2026, 11, 8, 18, 30, 0, DateTimeKind.Utc)));
        vm.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_ThatSucceeds_FinishesWithTrue()
    {
        _service.Setup(s => s.RestoreAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecoveryRestoreOutcome.Restored);
        bool? finished = null;
        var vm = Build();
        vm.Finished = restored => finished = restored;

        await vm.RestoreCommand.ExecuteAsync(null);

        finished.Should().BeTrue();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_ThatFails_StaysOnThePage_WithTheReason()
    {
        _service.Setup(s => s.RestoreAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecoveryRestoreOutcome.KeyMismatch);
        bool? finished = null;
        var vm = Build();
        vm.Finished = restored => finished = restored;

        await vm.RestoreCommand.ExecuteAsync(null);

        finished.Should().BeNull("the person may try again or start fresh");
        vm.HasError.Should().BeTrue();
        vm.ErrorText.Should().Be(AppResources.RecoveryOutcomeKeyMismatch);
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_ThatThrows_IsReportedNotRethrown()
    {
        _service.Setup(s => s.RestoreAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var vm = Build();

        await vm.RestoreCommand.ExecuteAsync(null);

        vm.ErrorText.Should().Be(AppResources.RecoveryOutcomeFailed);
    }

    [Fact]
    public void StartFresh_FinishesWithFalse_WithoutTouchingTheService()
    {
        bool? finished = null;
        var vm = Build();
        vm.Finished = restored => finished = restored;

        vm.StartFreshCommand.Execute(null);

        finished.Should().BeFalse();
        _service.Verify(s => s.RestoreAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
