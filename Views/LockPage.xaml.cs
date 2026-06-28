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
}
