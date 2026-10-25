using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diarion.Services.Store;

/// <summary>
/// The C# half of the facade contract in <c>native/ios-storekit/DiarionStoreKit.swift</c>. Lives
/// in Core, not beside the P/Invokes, so the shapes are tested on Windows; the Swift side only
/// serialises and this side only parses, and neither decides anything (spec 15, R3.2).
/// Unknown strings map to the <c>Unknown</c>/<c>None</c> member rather than throwing: a new
/// StoreKit value must not read as a broken app.
/// </summary>
public static class StoreKitJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<StoreProduct> Products(string json) =>
        Deserialize<List<ProductDto>>(json).Select(p => new StoreProduct(
            p.Id ?? string.Empty,
            p.DisplayName ?? string.Empty,
            p.DisplayPrice ?? string.Empty,
            ParsePrice(p.Price),
            p.CurrencyCode ?? string.Empty,
            p.Period is null ? null : Period(p.Period),
            p.IntroOffer is null ? null : new StoreIntroOffer(
                IntroKind(p.IntroOffer.Kind),
                Period(p.IntroOffer.Period ?? new PeriodDto()),
                p.IntroOffer.PeriodCount,
                ParsePrice(p.IntroOffer.Price),
                p.IntroOffer.DisplayPrice ?? string.Empty),
            p.IsEligibleForIntroOffer)).ToList();

    public static IReadOnlyList<StoreEntitlement> Entitlements(string json) =>
        Deserialize<List<EntitlementDto>>(json).Select(Entitlement).ToList();

    public static IReadOnlyList<SubscriptionStatus> Statuses(string json) =>
        Deserialize<List<StatusDto>>(json).Select(s => new SubscriptionStatus(
            s.ProductId ?? string.Empty,
            RenewalState(s.State),
            s.WillAutoRenew,
            Utc(s.GracePeriodExpiresAt))).ToList();

    public static AppTransactionInfo? AppTransaction(string json)
    {
        var dto = JsonSerializer.Deserialize<AppTransactionDto>(json, Options);
        return dto is null
            ? null
            : new AppTransactionInfo(dto.OriginalAppVersion ?? string.Empty, Environment(dto.Environment), Utc(dto.OriginalPurchaseDate));
    }

    public static PurchaseOutcome Purchase(string json)
    {
        var dto = JsonSerializer.Deserialize<PurchaseDto>(json, Options) ?? new PurchaseDto();
        return dto.State switch
        {
            "purchased" when dto.Entitlement is not null => PurchaseOutcome.Purchased(Entitlement(dto.Entitlement)),
            "cancelled" => PurchaseOutcome.Cancelled,
            _ => PurchaseOutcome.Pending
        };
    }

    private static T Deserialize<T>(string json) where T : new() =>
        JsonSerializer.Deserialize<T>(json, Options) ?? new T();

    private static StoreEntitlement Entitlement(EntitlementDto e) => new(
        e.ProductId ?? string.Empty,
        Utc(e.ExpiresAt),
        OfferType(e.OfferType),
        Environment(e.Environment),
        Utc(e.RevokedAt));

    private static SubscriptionPeriod Period(PeriodDto p) => new(p.Unit switch
    {
        "week" => SubscriptionPeriodUnit.Week,
        "month" => SubscriptionPeriodUnit.Month,
        "year" => SubscriptionPeriodUnit.Year,
        _ => SubscriptionPeriodUnit.Day
    }, p.Count);

    private static StoreIntroOfferKind IntroKind(string? kind) => kind switch
    {
        "freeTrial" => StoreIntroOfferKind.FreeTrial,
        "payAsYouGo" => StoreIntroOfferKind.PayAsYouGo,
        _ => StoreIntroOfferKind.PayUpFront
    };

    private static StoreOfferType OfferType(string? type) => type switch
    {
        "introductory" => StoreOfferType.Introductory,
        "promotional" => StoreOfferType.Promotional,
        "code" => StoreOfferType.Code,
        "winBack" => StoreOfferType.WinBack,
        _ => StoreOfferType.None
    };

    private static StoreEnvironment Environment(string? environment) => environment switch
    {
        "production" => StoreEnvironment.Production,
        "sandbox" => StoreEnvironment.Sandbox,
        "xcode" => StoreEnvironment.Xcode,
        _ => StoreEnvironment.Unknown
    };

    private static SubscriptionRenewalState RenewalState(string? state) => state switch
    {
        "subscribed" => SubscriptionRenewalState.Subscribed,
        "expired" => SubscriptionRenewalState.Expired,
        "inBillingRetry" => SubscriptionRenewalState.InBillingRetry,
        "inGracePeriod" => SubscriptionRenewalState.InGracePeriod,
        "revoked" => SubscriptionRenewalState.Revoked,
        _ => SubscriptionRenewalState.Unknown
    };

    private static decimal ParsePrice(string? price) =>
        decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    /// <summary>ISO 8601 with a Z parses as UTC already; anything else is pinned to UTC rather than
    /// shifted, because the facade only ever writes UTC.</summary>
    private static DateTime? Utc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value, DateTimeKind.Utc);

    private sealed class PeriodDto
    {
        public string? Unit { get; set; }
        public int Count { get; set; }
    }

    private sealed class IntroOfferDto
    {
        public string? Kind { get; set; }
        public PeriodDto? Period { get; set; }
        public int PeriodCount { get; set; }
        public string? Price { get; set; }
        public string? DisplayPrice { get; set; }
    }

    private sealed class ProductDto
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? DisplayPrice { get; set; }
        public string? Price { get; set; }
        public string? CurrencyCode { get; set; }
        public PeriodDto? Period { get; set; }
        public IntroOfferDto? IntroOffer { get; set; }
        public bool IsEligibleForIntroOffer { get; set; }
    }

    private sealed class EntitlementDto
    {
        public string? ProductId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? OfferType { get; set; }
        public string? Environment { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    private sealed class StatusDto
    {
        public string? ProductId { get; set; }
        public string? State { get; set; }
        public bool WillAutoRenew { get; set; }
        public DateTime? GracePeriodExpiresAt { get; set; }
    }

    private sealed class AppTransactionDto
    {
        public string? OriginalAppVersion { get; set; }
        public string? Environment { get; set; }
        public DateTime? OriginalPurchaseDate { get; set; }
    }

    private sealed class PurchaseDto
    {
        public string? State { get; set; }
        public EntitlementDto? Entitlement { get; set; }
    }
}
