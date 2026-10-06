using System;
using System.Threading.Tasks;
using Diarion.Services;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class LockViewModelTests
{
    private static LockViewModel Create(Mock<IAppLockService> lockMock, Mock<IBiometricService>? bioMock = null)
    {
        bioMock ??= new Mock<IBiometricService>();
        return new LockViewModel(lockMock.Object, bioMock.Object);
    }

    private static void EnterPin(LockViewModel vm, string pin)
    {
        foreach (var c in pin)
        {
            vm.EnterDigitCommand.Execute(c.ToString());
        }
    }

    [Fact]
    public void CorrectPin_InvokesUnlocked()
    {
        var lockMock = new Mock<IAppLockService>();
        lockMock.SetupGet(s => s.LockoutRemaining).Returns((TimeSpan?)null);
        lockMock.Setup(s => s.VerifyPin("1234")).Returns(PinVerifyResult.Success);

        var vm = Create(lockMock);
        var unlocked = false;
        vm.Unlocked = () => unlocked = true;

        EnterPin(vm, "1234");

        unlocked.Should().BeTrue();
    }

    [Fact]
    public void WrongPin_DoesNotUnlock_AndClearsBuffer()
    {
        var lockMock = new Mock<IAppLockService>();
        lockMock.SetupGet(s => s.LockoutRemaining).Returns((TimeSpan?)null);
        lockMock.Setup(s => s.VerifyPin(It.IsAny<string>())).Returns(PinVerifyResult.Wrong);

        var vm = Create(lockMock);
        var unlocked = false;
        vm.Unlocked = () => unlocked = true;

        EnterPin(vm, "0000");

        unlocked.Should().BeFalse();
        vm.EnteredCount.Should().Be(0);
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void LockedOut_BlocksInput_WithoutVerifying()
    {
        var lockMock = new Mock<IAppLockService>();
        lockMock.SetupGet(s => s.LockoutRemaining).Returns(TimeSpan.FromSeconds(30));

        var vm = Create(lockMock);
        var unlocked = false;
        vm.Unlocked = () => unlocked = true;

        vm.EnterDigitCommand.Execute("1");

        vm.EnteredCount.Should().Be(0);
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        unlocked.Should().BeFalse();
        lockMock.Verify(s => s.VerifyPin(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BiometricSuccess_InvokesUnlocked()
    {
        var lockMock = new Mock<IAppLockService>();
        lockMock.SetupGet(s => s.IsBiometricEnabled).Returns(true);

        var bioMock = new Mock<IBiometricService>();
        bioMock.Setup(b => b.IsAvailableAsync()).ReturnsAsync(true);
        bioMock.Setup(b => b.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var vm = Create(lockMock, bioMock);
        var unlocked = false;
        vm.Unlocked = () => unlocked = true;

        await vm.OnAppearingAsync();

        unlocked.Should().BeTrue();
    }

    [Fact]
    public async Task BiometricUnavailable_StaysOnPin_FailClosed()
    {
        var lockMock = new Mock<IAppLockService>();
        lockMock.SetupGet(s => s.IsBiometricEnabled).Returns(true);

        var bioMock = new Mock<IBiometricService>();
        bioMock.Setup(b => b.IsAvailableAsync()).ReturnsAsync(false);

        var vm = Create(lockMock, bioMock);
        var unlocked = false;
        vm.Unlocked = () => unlocked = true;

        await vm.OnAppearingAsync();

        vm.IsBiometricAvailable.Should().BeFalse();
        unlocked.Should().BeFalse();
        bioMock.Verify(b => b.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
