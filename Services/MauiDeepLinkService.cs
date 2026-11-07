using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Services.Widgets;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Diarion.Services;

/// <inheritdoc />
/// <remarks>
/// One pending link at a time: a second tap before the first is played replaces it, since the user
/// evidently changed their mind. "Ready" means the window's root is the Shell and neither the lock nor
/// onboarding is on top of it — the lock in particular must never be skipped by a link (spec 20, D3),
/// and <c>App</c> calls <see cref="FlushPending"/> the moment it comes down.
/// </remarks>
public sealed class MauiDeepLinkService : IDeepLinkService
{
    private string? _pending;

    public void Open(string link)
    {
        if (!WidgetDeepLink.TryParse(link, out _)) return;
        _pending = link;
        FlushPending();
    }

    public void FlushPending()
    {
        if (_pending is null) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var link = _pending;
            if (link is null || !IsReady()) return;
            _pending = null;
            try
            {
                await NavigateAsync(link);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Deep link {link} not followed: {ex}");
            }
        });
    }

    private static bool IsReady()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Page is not Shell shell) return false;
        var top = shell.Navigation?.ModalStack?.LastOrDefault();
        return top is not (Views.LockPage or Views.OnboardingPage);
    }

    private static async Task NavigateAsync(string link)
    {
        if (!WidgetDeepLink.TryParse(link, out var destination)) return;
        var shell = Shell.Current;
        if (shell is null) return;

        // Every destination starts from the home tab; that also pops whatever detail page was open.
        await shell.GoToAsync("//MainPage");

        // The day's habits are a block of the diary page, not the "HabitTracker" route — that one is
        // the quit tracker. So Today and Habits both land on the diary half, Tasks on the planner half.
        WeakReferenceMessenger.Default.Send(new NavigateToDateMessage(DateTime.Today));
        WeakReferenceMessenger.Default.Send(new HomeSectionRequestedMessage(
            destination == WidgetDestination.Tasks ? HomeSection.Planner : HomeSection.Diary));
    }
}
