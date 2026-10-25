using System;
using Diarion.Services.Store;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The wire shapes DiarionStoreKit.swift writes, parsed on this side. The samples are what the
/// Swift tests assert the facade produces; if either side moves, one of the two suites goes red.
/// </summary>
public class StoreKitJsonTests
{
    [Fact]
    public void Products_parse_prices_as_decimals_and_periods_as_units()
    {
        const string json = """
            [{"id":"diarion.base.monthly","displayName":"Diarion Base (month)","displayPrice":"$2.49",
              "price":"2.49","currencyCode":"USD","period":{"unit":"month","count":1},
              "introOffer":{"kind":"freeTrial","period":{"unit":"week","count":2},"periodCount":1,"price":"0","displayPrice":"$0.00"},
              "isEligibleForIntroOffer":true},
             {"id":"diarion.base.yearly","displayName":"Diarion Base (year)","displayPrice":"$11.99",
              "price":"11.99","currencyCode":"USD","period":{"unit":"year","count":1},
              "introOffer":null,"isEligibleForIntroOffer":false}]
            """;

        var products = StoreKitJson.Products(json);

        products.Should().HaveCount(2);
        var monthly = products[0];
        monthly.Price.Should().Be(2.49m);
        monthly.DisplayPrice.Should().Be("$2.49");
        monthly.Period.Should().Be(new SubscriptionPeriod(SubscriptionPeriodUnit.Month, 1));
        monthly.IntroOffer.Should().NotBeNull();
        monthly.IntroOffer!.Kind.Should().Be(StoreIntroOfferKind.FreeTrial);
        monthly.IntroOffer.Period.Should().Be(new SubscriptionPeriod(SubscriptionPeriodUnit.Week, 2));
        monthly.IsEligibleForIntroOffer.Should().BeTrue();

        var yearly = products[1];
        yearly.Price.Should().Be(11.99m);
        yearly.IntroOffer.Should().BeNull();
        yearly.IsEligibleForIntroOffer.Should().BeFalse();
    }

    [Fact]
    public void Prices_are_parsed_invariantly_whatever_the_device_culture()
    {
        using var _ = new CultureScope("uk-UA");

        StoreKitJson.Products("""[{"id":"x","price":"2.49"}]""")[0].Price.Should().Be(2.49m);
    }

    [Fact]
    public void Entitlements_carry_utc_dates_and_named_offer_types()
    {
        const string json = """
            [{"productId":"diarion.base.yearly","expiresAt":"2027-10-06T10:00:00Z","offerType":"introductory",
              "environment":"production","revokedAt":null}]
            """;

        var entitlement = StoreKitJson.Entitlements(json).Should().ContainSingle().Subject;

        entitlement.ProductId.Should().Be(StoreCatalog.BaseYearly);
        entitlement.ExpiresAt.Should().Be(new DateTime(2027, 10, 6, 10, 0, 0, DateTimeKind.Utc));
        entitlement.ExpiresAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        entitlement.OfferType.Should().Be(StoreOfferType.Introductory);
        entitlement.Environment.Should().Be(StoreEnvironment.Production);
        entitlement.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void A_refund_keeps_its_revocation_date()
    {
        const string json = """[{"productId":"diarion.base.monthly","expiresAt":"2026-11-06T10:00:00Z","offerType":"none","environment":"sandbox","revokedAt":"2026-10-07T09:30:00Z"}]""";

        StoreKitJson.Entitlements(json)[0].RevokedAt.Should().Be(new DateTime(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Statuses_parse_the_renewal_state_and_grace_deadline()
    {
        const string json = """[{"productId":"diarion.base.monthly","state":"inGracePeriod","willAutoRenew":true,"gracePeriodExpiresAt":"2026-10-20T00:00:00Z"}]""";

        var status = StoreKitJson.Statuses(json).Should().ContainSingle().Subject;

        status.State.Should().Be(SubscriptionRenewalState.InGracePeriod);
        status.WillAutoRenew.Should().BeTrue();
        status.GracePeriodExpiresAt.Should().Be(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void The_app_receipt_parses_in_every_environment()
    {
        StoreKitJson.AppTransaction("""{"originalAppVersion":"2","environment":"production","originalPurchaseDate":"2026-10-06T01:40:00Z"}""")
            .Should().Be(new AppTransactionInfo("2", StoreEnvironment.Production, new DateTime(2026, 10, 6, 1, 40, 0, DateTimeKind.Utc)));

        StoreKitJson.AppTransaction("""{"originalAppVersion":"1.0","environment":"sandbox","originalPurchaseDate":null}""")
            .Should().Be(new AppTransactionInfo("1.0", StoreEnvironment.Sandbox, null));
    }

    [Theory]
    [InlineData("""{"state":"cancelled","entitlement":null}""", PurchaseResultKind.Cancelled)]
    [InlineData("""{"state":"pending","entitlement":null}""", PurchaseResultKind.Pending)]
    [InlineData("""{"state":"purchased","entitlement":null}""", PurchaseResultKind.Pending)]
    [InlineData("""{"state":"something new"}""", PurchaseResultKind.Pending)]
    public void A_purchase_without_an_entitlement_never_counts_as_owned(string json, PurchaseResultKind expected)
    {
        StoreKitJson.Purchase(json).Kind.Should().Be(expected);
    }

    [Fact]
    public void A_completed_purchase_carries_its_entitlement()
    {
        const string json = """{"state":"purchased","entitlement":{"productId":"diarion.base.monthly","expiresAt":"2026-10-20T00:00:00Z","offerType":"introductory","environment":"xcode","revokedAt":null}}""";

        var outcome = StoreKitJson.Purchase(json);

        outcome.Kind.Should().Be(PurchaseResultKind.Purchased);
        outcome.Entitlement!.ProductId.Should().Be(StoreCatalog.BaseMonthly);
        outcome.Entitlement.Environment.Should().Be(StoreEnvironment.Xcode);
    }

    [Fact]
    public void Unknown_words_map_to_unknown_instead_of_throwing()
    {
        var entitlement = StoreKitJson.Entitlements("""[{"productId":"p","offerType":"loyalty","environment":"mars"}]""")[0];
        entitlement.OfferType.Should().Be(StoreOfferType.None);
        entitlement.Environment.Should().Be(StoreEnvironment.Unknown);

        StoreKitJson.Statuses("""[{"productId":"p","state":"paused"}]""")[0].State.Should().Be(SubscriptionRenewalState.Unknown);
        StoreKitJson.Products("""[{"id":"p","period":{"unit":"fortnight","count":1},"introOffer":{"kind":"gift"}}]""")[0]
            .IntroOffer!.Kind.Should().Be(StoreIntroOfferKind.PayUpFront);
    }

    [Fact]
    public void Empty_answers_are_empty_lists_not_nulls()
    {
        StoreKitJson.Products("[]").Should().BeEmpty();
        StoreKitJson.Entitlements("[]").Should().BeEmpty();
        StoreKitJson.Statuses("[]").Should().BeEmpty();
        StoreKitJson.AppTransaction("null").Should().BeNull();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentCulture;

        public CultureScope(string name) =>
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);

        public void Dispose() => System.Globalization.CultureInfo.CurrentCulture = _previous;
    }
}
