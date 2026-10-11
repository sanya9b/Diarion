using System.Globalization;

namespace Diarion.Services.Store;

/// <summary>
/// The two pages the paywall and the store listing must link to (spec 15, R4.1, R6.2). Same
/// addresses App Store Connect has, one per language, so the page opens in the language the app
/// is shown in.
/// </summary>
public static class StoreLinks
{
    private const string Site = "https://diarion.org/";

    public static Uri Terms(CultureInfo culture) => new(Site + Prefix(culture) + "terms");

    public static Uri Privacy(CultureInfo culture) => new(Site + Prefix(culture) + "privacy");

    private static string Prefix(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "uk" ? string.Empty : "en/";
}
