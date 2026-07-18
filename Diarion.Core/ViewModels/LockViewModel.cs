using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class LockViewModel : ObservableObject
{
    public const int PinLength = 4;

    private readonly IAppLockService _lockService;
    private readonly IBiometricService _biometricService;
    private string _pinBuffer = string.Empty;

    /// <summary>Invoked when the user successfully unlocks (via PIN or biometrics).</summary>
    public Action? Unlocked { get; set; }

    [ObservableProperty] private int _enteredCount;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBiometricAvailable;

    /// <summary>Filled/empty dots reflecting how many digits have been entered (e.g. "●●○○").</summary>
    public string PinDisplay => new string('●', EnteredCount) + new string('○', PinLength - EnteredCount);

    partial void OnEnteredCountChanged(int value) => OnPropertyChanged(nameof(PinDisplay));

    public LockViewModel(IAppLockService lockService, IBiometricService biometricService)
    {
        _lockService = lockService;
        _biometricService = biometricService;
    }

    public async Task OnAppearingAsync()
    {
        IsBiometricAvailable = _lockService.IsBiometricEnabled && await _biometricService.IsAvailableAsync();
        if (IsBiometricAvailable)
        {
            await TryBiometricAsync();
        }
    }

    [RelayCommand]
    public async Task TryBiometricAsync()
    {
        if (!IsBiometricAvailable)
        {
            return;
        }

        var ok = await _biometricService.AuthenticateAsync(
            Diarion.Resources.Localization.AppResources.SecurityLabel,
            Diarion.Resources.Localization.AppResources.BiometricPromptReason);

        // Fail-closed: on failure we simply stay on the PIN screen — never bypass.
        if (ok)
        {
            Unlocked?.Invoke();
        }
    }

    [RelayCommand]
    public void EnterDigit(string digit)
    {
        if (IsLockedOut(out var remaining))
        {
            ShowLockout(remaining);
            return;
        }

        if (_pinBuffer.Length >= PinLength)
        {
            return;
        }

        ErrorMessage = string.Empty;
        _pinBuffer += digit;
        EnteredCount = _pinBuffer.Length;

        if (_pinBuffer.Length == PinLength)
        {
            Submit();
        }
    }

    [RelayCommand]
    public void DeleteDigit()
    {
        if (_pinBuffer.Length > 0)
        {
            _pinBuffer = _pinBuffer[..^1];
            EnteredCount = _pinBuffer.Length;
        }
    }

    private void Submit()
    {
        var result = _lockService.VerifyPin(_pinBuffer);
        _pinBuffer = string.Empty;
        EnteredCount = 0;

        switch (result)
        {
            case PinVerifyResult.Success:
                ErrorMessage = string.Empty;
                Unlocked?.Invoke();
                break;
            case PinVerifyResult.LockedOut:
                ShowLockout(_lockService.LockoutRemaining ?? TimeSpan.Zero);
                break;
            default:
                ErrorMessage = Diarion.Resources.Localization.AppResources.PinIncorrectMessage;
                break;
        }
    }

    private bool IsLockedOut(out TimeSpan remaining)
    {
        remaining = _lockService.LockoutRemaining ?? TimeSpan.Zero;
        return remaining > TimeSpan.Zero;
    }

    private void ShowLockout(TimeSpan remaining)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        ErrorMessage = string.Format(Diarion.Resources.Localization.AppResources.PinLockedOutMessage, seconds);
    }
}
