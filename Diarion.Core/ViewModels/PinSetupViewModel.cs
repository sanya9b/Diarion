using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// Drives the full-screen PIN set/change flow (same keypad visuals as <see cref="LockViewModel"/>)
/// so the app never falls back to the plain OS prompt dialogs.
/// Steps: verify current PIN (only when one already exists) → enter new PIN → repeat new PIN.
/// </summary>
public partial class PinSetupViewModel : ObservableObject
{
    public const int PinLength = 4;

    private readonly IAppLockService _lockService;

    private enum Step { VerifyCurrent, EnterNew, ConfirmNew }

    private Step _step;
    private string _buffer = string.Empty;
    private string _newPin = string.Empty;

    /// <summary>Invoked after the PIN is successfully set (the page should navigate back).</summary>
    public Action? Completed { get; set; }

    /// <summary>Invoked when the user cancels (the page should navigate back).</summary>
    public Action? Cancelled { get; set; }

    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private int _enteredCount;

    /// <summary>Filled/empty dots reflecting how many digits have been entered (e.g. "●●○○").</summary>
    public string PinDisplay => new string('●', EnteredCount) + new string('○', PinLength - EnteredCount);

    partial void OnEnteredCountChanged(int value) => OnPropertyChanged(nameof(PinDisplay));

    public PinSetupViewModel(IAppLockService lockService)
    {
        _lockService = lockService;
    }

    /// <summary>Resets state and picks the starting step based on whether a PIN already exists.</summary>
    public void Initialize()
    {
        _buffer = string.Empty;
        _newPin = string.Empty;
        EnteredCount = 0;
        ErrorMessage = string.Empty;
        _step = _lockService.IsLockEnabled ? Step.VerifyCurrent : Step.EnterNew;
        UpdatePrompt();
    }

    [RelayCommand]
    private void EnterDigit(string digit)
    {
        if (_buffer.Length >= PinLength) return;

        ErrorMessage = string.Empty;
        _buffer += digit;
        EnteredCount = _buffer.Length;

        if (_buffer.Length == PinLength)
        {
            Advance();
        }
    }

    [RelayCommand]
    private void DeleteDigit()
    {
        if (_buffer.Length == 0) return;
        _buffer = _buffer[..^1];
        EnteredCount = _buffer.Length;
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    private void Advance()
    {
        var entered = _buffer;
        _buffer = string.Empty;
        EnteredCount = 0;

        switch (_step)
        {
            case Step.VerifyCurrent:
                var result = _lockService.VerifyPin(entered);
                if (result == PinVerifyResult.Success)
                {
                    _step = Step.EnterNew;
                }
                else if (result == PinVerifyResult.LockedOut)
                {
                    ShowLockout();
                }
                else
                {
                    ErrorMessage = Diarion.Resources.Localization.AppResources.PinIncorrectMessage;
                }
                break;

            case Step.EnterNew:
                _newPin = entered;
                _step = Step.ConfirmNew;
                break;

            case Step.ConfirmNew:
                if (entered == _newPin)
                {
                    _lockService.SetPin(_newPin);
                    Completed?.Invoke();
                    return;
                }

                // Mismatch: restart from the "enter new PIN" step so the user re-enters both.
                ErrorMessage = Diarion.Resources.Localization.AppResources.PinMismatchMessage;
                _newPin = string.Empty;
                _step = Step.EnterNew;
                break;
        }

        UpdatePrompt();
    }

    private void UpdatePrompt()
    {
        Prompt = _step switch
        {
            Step.VerifyCurrent => Diarion.Resources.Localization.AppResources.EnterCurrentPinPrompt,
            Step.EnterNew => Diarion.Resources.Localization.AppResources.EnterNewPinPrompt,
            Step.ConfirmNew => Diarion.Resources.Localization.AppResources.ConfirmPinPrompt,
            _ => string.Empty
        };
    }

    private void ShowLockout()
    {
        var remaining = _lockService.LockoutRemaining ?? TimeSpan.Zero;
        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        ErrorMessage = string.Format(Diarion.Resources.Localization.AppResources.PinLockedOutMessage, seconds);
    }
}
