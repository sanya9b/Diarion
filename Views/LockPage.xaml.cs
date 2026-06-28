using Microsoft.Maui.Controls;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;
using System;

namespace Diarion.Views;

public partial class LockPage : ContentPage
{
    private readonly Action _onUnlocked;

    public LockPage(Action onUnlocked)
    {
        InitializeComponent();
        _onUnlocked = onUnlocked;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AuthenticateAsync();
    }

    private async void OnUnlockClicked(object? sender, EventArgs e)
    {
        await AuthenticateAsync();
    }

    private async System.Threading.Tasks.Task AuthenticateAsync()
    {
        try
        {
            var isAvailable = await CrossFingerprint.Current.IsAvailableAsync();
            if (!isAvailable)
            {
                // If biometrics not available, bypass or show alert to prevent lock screen lock
                _onUnlocked?.Invoke();
                return;
            }

            var result = await CrossFingerprint.Current.AuthenticateAsync(new AuthenticationRequestConfiguration(
                Diarion.Resources.Localization.AppResources.SecurityLabel,
                Diarion.Resources.Localization.AppResources.BiometricPromptReason)
            {
                AllowAlternativeAuthentication = true
            });

            if (result.Authenticated)
            {
                _onUnlocked?.Invoke();
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Biometric auth exception: {ex.Message}");
            // Safety fallback: allow bypass in case of persistent exception to prevent permanent lockout
            _onUnlocked?.Invoke();
        }
    }
}
