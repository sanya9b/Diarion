using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Diarion.Services;

/// <summary>A currency the app can display, with how its symbol is written.</summary>
public sealed record Currency(string Code, string Symbol, bool SymbolBefore)
{
    public string DisplayName => Symbol == Code ? Code : $"{Code}  {Symbol}";
}

/// <summary>
/// Renders money for display. One place, so an amount reads the same on the feed, the budget card and
/// the statistics tile.
/// <para>
/// There is a single currency per profile and no conversion anywhere. That is not a shortcut: turning
/// per-account currencies into a total requires exchange rates, rates have to be fetched, and this app
/// deliberately cannot reach the network. A number converted at a rate the app made up would be worse
/// than no feature at all.
/// </para>
/// </summary>
public static class MoneyFormatter
{
    public const string FallbackCode = "UAH";

    /// <summary>
    /// A curated list rather than every ISO code: this feeds a picker, and the symbols have to be
    /// hand-checked because .NET offers no mapping from a currency code to its symbol.
    /// </summary>
    public static IReadOnlyList<Currency> Supported { get; } = new List<Currency>
    {
        new("UAH", "₴", false),
        new("EUR", "€", false),
        new("USD", "$", true),
        new("GBP", "£", true),
        new("PLN", "zł", false),
        new("CZK", "Kč", false),
        new("CHF", "CHF", false),
        new("CAD", "C$", true),
        new("AUD", "A$", true),
        new("JPY", "¥", true),
        new("TRY", "₺", false),
        new("SEK", "kr", false),
        new("NOK", "kr", false),
        new("RON", "lei", false),
        new("HUF", "Ft", false),
    };

    /// <summary>
    /// The currency for a code, or one built from the code itself when it is not in the list. An
    /// unknown code prints as the code, which is unlovely but never wrong.
    /// </summary>
    public static Currency Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Supported.First(c => c.Code == FallbackCode);
        }

        var trimmed = code.Trim().ToUpperInvariant();
        return Supported.FirstOrDefault(c => c.Code == trimmed)
               ?? new Currency(trimmed, trimmed, false);
    }

    /// <summary>
    /// What the device suggests, used only until the user picks one. Wrapped because the region is
    /// unavailable on some configurations and a missing region must not stop the app.
    /// </summary>
    public static string DeviceDefaultCode()
    {
        try
        {
            // A neutral culture (plain "en" after the language toggle) has no country: on Windows its
            // region's currency "symbol" is the placeholder "¤¤", on Linux/ICU it is the world region
            // "001" with "XDR". Neither is a suggestion anyone wants, so the culture is checked first and
            // only a real, national ISO 4217 code gets through.
            var culture = CultureInfo.CurrentCulture;
            if (culture.IsNeutralCulture || string.IsNullOrEmpty(culture.Name)) return FallbackCode;
            var code = RegionInfo.CurrentRegion?.ISOCurrencySymbol;
            return IsSuggestibleCode(code) ? code! : FallbackCode;
        }
        catch
        {
            return FallbackCode;
        }
    }

    /// <summary>
    /// A national ISO 4217 code: three capital letters, not one of the X-codes (XDR, XXX, XAU…), which
    /// ICU hands out for supranational regions and which no shop prices anything in.
    /// </summary>
    public static bool IsSuggestibleCode(string? code)
        => code is { Length: 3 } && code.All(char.IsAsciiLetterUpper) && code[0] != 'X';

    /// <summary>
    /// Groups and rounds to two decimals in the current culture, then attaches the symbol. A negative
    /// amount carries its sign in front of the whole thing ("-$ 1,450.00"), the same place
    /// <see cref="FormatSigned"/> puts it — the balance card used to print "$ -1,450.00" next to
    /// "-$ 250.00" on the same screen. A value that rounds to zero is not negative.
    /// </summary>
    public static string Format(decimal amount, string? currencyCode, CultureInfo? culture = null)
    {
        var number = Math.Abs(amount).ToString("N2", culture ?? CultureInfo.CurrentCulture);
        var text = Attach(number, currencyCode);
        return amount < 0 && Math.Round(amount, 2) != 0 ? "-" + text : text;
    }

    /// <summary>
    /// The magnitude with an explicit sign in front, for deltas where "no change" and "down a little"
    /// must not look alike.
    /// </summary>
    public static string FormatSigned(decimal amount, string? currencyCode, CultureInfo? culture = null)
    {
        var sign = amount < 0 ? "-" : "+";
        return sign + Format(Math.Abs(amount), currencyCode, culture);
    }

    private static string Attach(string number, string? currencyCode)
    {
        var currency = Resolve(currencyCode);

        // Non-breaking space: an amount that wraps between the number and its symbol is unreadable,
        // and these sit in narrow cards and chips.
        const string Nbsp = " ";

        return currency.SymbolBefore
            ? currency.Symbol + Nbsp + number
            : number + Nbsp + currency.Symbol;
    }
}
