using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace Diarion.Services;

public sealed class MauiExternalLinkService : IExternalLinkService
{
    public async Task OpenAsync(Uri uri)
    {
        try
        {
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            // No browser to hand the page to is not worth a crash on a paywall.
            System.Diagnostics.Debug.WriteLine($"Opening {uri} failed: {ex.Message}");
        }
    }
}
