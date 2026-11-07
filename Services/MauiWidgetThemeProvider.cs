using System;
using System.Collections.Concurrent;
using Diarion.Models.Widgets;
using Diarion.Services.Widgets;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Services;

/// <inheritdoc />
/// <remarks>
/// Reads the same dictionaries <see cref="ThemeManager"/> merges into the app, by the keys the cards
/// use: card surface, primary and secondary text, the divider, and the four accents, which the Pink
/// theme recolours too. Each dictionary is instantiated once and cached — a fresh instance rather than
/// <c>Application.Current.Resources</c>, so the read is thread-agnostic and never races the merge.
/// </remarks>
public sealed class MauiWidgetThemeProvider : IWidgetThemeProvider
{
    private readonly ConcurrentDictionary<string, WidgetTheme> _cache = new();

    public WidgetTheme? Current()
    {
        var name = ThemeManager.GetCurrentTheme();
        try
        {
            return _cache.GetOrAdd(name, Read);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Widget theme not read: {ex}");
            return null;
        }
    }

    private static WidgetTheme Read(string name)
    {
        ResourceDictionary dictionary = name switch
        {
            ThemeManager.ThemeDark => new Resources.Themes.DarkTheme(),
            ThemeManager.ThemePink => new Resources.Themes.PinkTheme(),
            _ => new Resources.Themes.LightTheme()
        };

        string Hex(string key, string fallback)
            => dictionary.TryGetValue(key, out var value) && value is Color color ? ToHex(color) : fallback;

        return new WidgetTheme
        {
            Name = name,
            Background = Hex("Theme_Snow_Black", "#FFFFFF"),
            Text = Hex("Theme_Midnight_Snow", "#22282C"),
            Secondary = Hex("Theme_Ocean_Dust", "#6B7680"),
            Track = Hex("Theme_Dust_Ocean", "#E0E6EA"),
            Accent = Hex("Theme_Coral", "#C26D53"),
            Sage = Hex("Theme_Sage", "#8FA083"),
            Amber = Hex("Theme_Amber", "#C9985A"),
            Ocean = Hex("Theme_Ocean_Ocean", "#929FA7")
        };
    }

    private static string ToHex(Color color)
        => $"#{(int)Math.Round(color.Red * 255):X2}{(int)Math.Round(color.Green * 255):X2}{(int)Math.Round(color.Blue * 255):X2}";
}
