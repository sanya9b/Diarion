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
            var code = RegionInfo.CurrentRegion?.ISOCurrencySymbol;
            return string.IsNullOrWhiteSpace(code) ? FallbackCode : code.ToUpperInvariant();
        }
        catch
        {
            return FallbackCode;
        }
    }

    /// <summary>Groups and rounds to two decimals in the current culture, then attaches the symbol.</summary>
    public static string Format(decimal amount, string? currencyCode, CultureInfo? culture = null)
    {
        var number = amount.ToString("N2", culture ?? CultureInfo.CurrentCulture);
        return Attach(number, currencyCode);
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
