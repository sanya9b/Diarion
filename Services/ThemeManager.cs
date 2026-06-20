using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Collections.Generic;

namespace Diarion.Services
{
    public static class ThemeManager
    {
        public const string KeyTheme = "AppThemePref";
        
        // Themes
        public const string ThemeLight = "Light";
        public const string ThemeDark = "Dark";
        public const string ThemePink = "Pink";

        public static void SetTheme(string themeName)
        {
            var app = Application.Current;
            if (app == null) return;

            // Save preference
            Preferences.Set(KeyTheme, themeName);

            // Set system theme preference (mainly for status bar and native controls)
            if (themeName == ThemeDark)
            {
                app.UserAppTheme = AppTheme.Dark;
            }
            else
            {
                app.UserAppTheme = AppTheme.Light; // Pink relies on Light for native elements
            }

            // Load appropriate ResourceDictionary
            ResourceDictionary themeDictionary = themeName switch
            {
                ThemeDark => new Resources.Themes.DarkTheme(),
                ThemePink => new Resources.Themes.PinkTheme(),
                _ => new Resources.Themes.LightTheme(),
            };

            // Remove existing theme dictionaries
            var mergedDictionaries = app.Resources.MergedDictionaries;
            var toRemove = new List<ResourceDictionary>();

            foreach (var dict in mergedDictionaries)
            {
                if (dict is Resources.Themes.LightTheme || dict is Resources.Themes.DarkTheme || dict is Resources.Themes.PinkTheme)
                {
                    toRemove.Add(dict);
                }
                else if (dict.Source != null && dict.Source.OriginalString.Contains("Theme.xaml"))
                {
                    toRemove.Add(dict);
                }
            }

            foreach (var dict in toRemove)
            {
                mergedDictionaries.Remove(dict);
            }

            // Add the new theme dictionary at the end so it overrides
            mergedDictionaries.Add(themeDictionary);
        }

        public static string GetCurrentTheme()
        {
            return Preferences.Get(KeyTheme, ThemeLight);
        }
    }
}
