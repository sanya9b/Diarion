using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Diarion.Services;

/// <summary>
/// Routes asked for from outside the app — a home-screen shortcut, a tap on the evening reminder —
/// held until there is a shell to navigate and nothing standing in front of it.
/// </summary>
/// <remarks>
/// <para>
/// Two things make the obvious <c>GoToAsync</c> wrong. On a cold start the request arrives before
/// the window exists, let alone the shell. And with the app lock on, the first page is the lock
/// screen, which must stay the first page: a sheet pushed over it would show the diary's habits
/// and today's mood to whoever is holding the phone. So the request is parked, and every point
/// where the shell becomes reachable — window created, lock dismissed, onboarding closed, app
/// resumed — asks for it to be flushed.
/// </para>
/// <para>
/// None of these entry points read anything outside the app process or show anything past the
/// PIN, which is what separates them from the widgets the roadmap declined.
/// </para>
/// </remarks>
public static class LaunchActions
{
    /// <summary>Identifier of the one app action the app registers with the platform.</summary>
    public const string QuickCaptureActionId = "quick-capture";

    private static readonly object Gate = new();
    private static string? _pendingRoute;

    /// <summary>Set by the application: true while the lock screen is the thing on screen.</summary>
    public static Func<bool>? IsBlocked { get; set; }

    public static void Request(string route)
    {
        lock (Gate)
        {
            _pendingRoute = route;
        }

        TryFlush();
    }

    /// <summary>Navigates to the parked route if the shell is up and unobstructed; otherwise leaves it parked.</summary>
    public static void TryFlush()
    {
        string? route;
        lock (Gate)
        {
            route = _pendingRoute;
        }

        if (route is null) return;

        MainThread.BeginInvokeOnMainThread(() => _ = FlushOnMainThreadAsync(route));
    }

    private static async Task FlushOnMainThreadAsync(string route)
    {
        var shell = Shell.Current;
        if (shell is null || (IsBlocked?.Invoke() ?? false)) return;

        // A modal already up — onboarding, the paywall, the sheet itself from a second tap — means
        // not now. The next flush point will try again; a second copy of the sheet never helps.
        if (shell.Navigation.ModalStack.Count > 0)
        {
            if (shell.Navigation.ModalStack.Any(page => page is Views.QuickCapturePage))
            {
                Clear(route);
            }

            return;
        }

        lock (Gate)
        {
            if (_pendingRoute != route) return;
            _pendingRoute = null;
        }

        try
        {
            await shell.GoToAsync(route);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Launch action '{route}' failed: {ex.Message}");
        }
    }

    private static void Clear(string route)
    {
        lock (Gate)
        {
            if (_pendingRoute == route) _pendingRoute = null;
        }
    }
}
