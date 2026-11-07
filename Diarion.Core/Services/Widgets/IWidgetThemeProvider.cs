using Diarion.Models.Widgets;

namespace Diarion.Services.Widgets;

/// <summary>
/// The app's current theme as widget colours (spec 20, D7). The app chooses Light, Dark or Pink
/// explicitly, so the widgets follow that choice rather than the system appearance; the provider
/// reads the theme dictionary and the coordinator republishes when the theme is switched.
/// </summary>
public interface IWidgetThemeProvider
{
    /// <summary>The palette of the active theme, or null where the app has no themes to read (tests, tools).</summary>
    WidgetTheme? Current();
}

/// <summary>No theme: the widgets fall back to their own light/dark palette.</summary>
public sealed class NullWidgetThemeProvider : IWidgetThemeProvider
{
    public WidgetTheme? Current() => null;
}
