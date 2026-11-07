using System;

namespace Diarion.Services.Widgets;

/// <summary>Where a tap on a widget lands in the app (spec 20, D3). Each one is a page the app already has.</summary>
public enum WidgetDestination
{
    /// <summary>The home screen on today's entry.</summary>
    Today,

    /// <summary>The home screen in planner mode, on today.</summary>
    Tasks,

    /// <summary>The home screen on today's entry, where the day's habits are ticked.</summary>
    Habits
}

/// <summary>
/// The <c>diarion://</c> links widgets open the app with. Parsing lives here, in Core, so it is tested
/// once and both platforms' widgets build the same strings; the navigation itself needs the Shell and
/// lives behind <see cref="IDeepLinkService"/> in the app project.
/// </summary>
public static class WidgetDeepLink
{
    public const string Scheme = "diarion";

    public static string For(WidgetDestination destination) => destination switch
    {
        WidgetDestination.Tasks => $"{Scheme}://tasks",
        WidgetDestination.Habits => $"{Scheme}://habits",
        _ => $"{Scheme}://today"
    };

    /// <summary>
    /// True for a link this app owns. Case and a trailing slash are forgiven; an unknown host or
    /// another scheme is not, so a widget from an older build can never steer a newer app somewhere
    /// it did not mean to go.
    /// </summary>
    public static bool TryParse(string? link, out WidgetDestination destination)
    {
        destination = WidgetDestination.Today;
        if (string.IsNullOrWhiteSpace(link)) return false;
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)) return false;

        switch (uri.Host.ToLowerInvariant())
        {
            case "today":
                destination = WidgetDestination.Today;
                return true;
            case "tasks":
                destination = WidgetDestination.Tasks;
                return true;
            case "habits":
                destination = WidgetDestination.Habits;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Opens a <c>diarion://</c> link inside the running app. The platform hands the link over as it
/// arrives — from an intent on Android, from the URL callback on iOS — and the service navigates when
/// it can: a link that arrives before the Shell exists, or while the lock screen is up, waits and is
/// played once the app is unlocked and on its feet.
/// </summary>
public interface IDeepLinkService
{
    /// <summary>Remembers the link and navigates now if the app is ready; otherwise on the next <see cref="FlushPending"/>.</summary>
    void Open(string link);

    /// <summary>Navigates to a link that was waiting, if any. Called after the Shell is shown and after an unlock.</summary>
    void FlushPending();
}
