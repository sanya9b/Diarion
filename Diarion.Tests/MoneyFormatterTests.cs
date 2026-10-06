using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class MoneyFormatterTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void A_trailing_symbol_currency_puts_the_symbol_after_the_number()
    {
        MoneyFormatter.Format(1234.5m, "UAH", Invariant).Should().Be("1,234.50 ₴");
    }

    [Fact]
    public void A_leading_symbol_currency_puts_it_in_front()
    {
        MoneyFormatter.Format(1234.5m, "USD", Invariant).Should().Be("$ 1,234.50");
    }

    [Fact]
    public void The_number_and_its_symbol_are_joined_by_a_non_breaking_space()
    {
        // These sit in narrow cards and chips; an amount that wraps away from its symbol is unreadable.
        MoneyFormatter.Format(5m, "EUR", Invariant).Should().NotContain(" ").And.Contain(" ");
    }

    [Fact]
    public void An_unknown_code_prints_as_the_code_rather_than_disappearing()
    {
        MoneyFormatter.Format(10m, "XYZ", Invariant).Should().Be("10.00 XYZ");
    }

    [Fact]
    public void A_missing_code_falls_back_rather_than_throwing()
    {
        MoneyFormatter.Format(10m, null, Invariant).Should().Contain("₴");
        MoneyFormatter.Format(10m, "  ", Invariant).Should().Contain("₴");
    }

    [Fact]
    public void Codes_are_matched_regardless_of_case_or_padding()
    {
        MoneyFormatter.Resolve(" usd ").Code.Should().Be("USD");
    }

    [Fact]
    public void Amounts_always_carry_two_decimals()
    {
        MoneyFormatter.Format(7m, "UAH", Invariant).Should().StartWith("7.00");
        MoneyFormatter.Format(7.005m, "UAH", Invariant).Should().StartWith("7.01");
    }

    [Fact]
    public void Grouping_follows_the_culture_not_the_currency()
    {
        var german = new CultureInfo("de-DE");

        MoneyFormatter.Format(1234.5m, "EUR", german).Should().Be("1.234,50 €");
    }

    [Fact]
    public void A_signed_amount_always_shows_its_direction()
    {
        // Without a forced plus, "no change" and "up a little" look identical at a glance.
        MoneyFormatter.FormatSigned(50m, "UAH", Invariant).Should().StartWith("+");
        MoneyFormatter.FormatSigned(-50m, "UAH", Invariant).Should().StartWith("-");
        MoneyFormatter.FormatSigned(0m, "UAH", Invariant).Should().StartWith("+");
    }

    [Fact]
    public void A_signed_amount_never_prints_two_minus_signs()
    {
        MoneyFormatter.FormatSigned(-50m, "UAH", Invariant).Should().Be("-50.00 ₴");
    }

    [Fact]
    public void Every_supported_currency_has_a_usable_symbol()
    {
        MoneyFormatter.Supported.Should().OnlyContain(c =>
            c.Code.Length == 3 && !string.IsNullOrWhiteSpace(c.Symbol));

        MoneyFormatter.Supported.Select(c => c.Code).Should().OnlyHaveUniqueItems();
        MoneyFormatter.Supported.Should().Contain(c => c.Code == MoneyFormatter.FallbackCode);
    }

    [Fact]
    public void A_profile_that_never_chose_a_currency_still_resolves_to_one()
    {
        // Existing profiles predate the field, so this has to work without a migration.
        var profile = new UserProfile();

        profile.CurrencyCode.Should().BeEmpty();
        profile.GetEffectiveCurrencyCode().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_explicit_choice_beats_the_device_default()
    {
        var profile = new UserProfile { CurrencyCode = "PLN" };

        profile.GetEffectiveCurrencyCode().Should().Be("PLN");
    }
}
