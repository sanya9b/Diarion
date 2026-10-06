namespace Diarion.Services.Store;

// Plain data crossing the store boundary. Mirrors StoreKit 2's vocabulary closely enough that the
// Swift facade only serialises, never interprets (spec 15, R3.2). All dates are UTC.

public enum SubscriptionPeriodUnit
{
    Day,
    Week,
    Month,
    Year
}

public sealed record SubscriptionPeriod(SubscriptionPeriodUnit Unit, int Count);

public enum StoreIntroOfferKind
{
    FreeTrial,
    PayAsYouGo,
    PayUpFront
}

public sealed record StoreIntroOffer(
    StoreIntroOfferKind Kind,
    SubscriptionPeriod Period,
    int PeriodCount,
    decimal Price,
    string DisplayPrice);

/// <param name="DisplayPrice">Formatted by the store in the storefront's currency — the only
/// string a price is ever shown from.</param>
/// <param name="IsEligibleForIntroOffer">False once this Apple ID has used any introductory offer in
/// the group; the paywall must then drop the word "free".</param>
public sealed record StoreProduct(
    string Id,
    string DisplayName,
    string DisplayPrice,
    decimal Price,
    string CurrencyCode,
    SubscriptionPeriod? Period,
    StoreIntroOffer? IntroOffer,
    bool IsEligibleForIntroOffer);

public enum StoreEnvironment
{
    Unknown,
    Production,
    Sandbox,
    Xcode
}

public enum StoreOfferType
{
    None,
    Introductory,
    Promotional,
    Code,
    WinBack
}

/// <summary>One verified transaction that currently entitles the user to a product.</summary>
public sealed record StoreEntitlement(
    string ProductId,
    DateTime? ExpiresAt,
    StoreOfferType OfferType,
    StoreEnvironment Environment,
    DateTime? RevokedAt);

public enum SubscriptionRenewalState
{
    Unknown,
    Subscribed,
    Expired,
    InBillingRetry,
    InGracePeriod,
    Revoked
}

public sealed record SubscriptionStatus(
    string ProductId,
    SubscriptionRenewalState State,
    bool WillAutoRenew,
    DateTime? GracePeriodExpiresAt);

/// <param name="OriginalAppVersion">On iOS this is the <c>CFBundleVersion</c> (build number) of the
/// first version this Apple ID downloaded; in the sandbox it is always the string "1.0".</param>
public sealed record AppTransactionInfo(
    string OriginalAppVersion,
    StoreEnvironment Environment,
    DateTime? OriginalPurchaseDate);

public enum PurchaseResultKind
{
    Purchased,
    /// <summary>Ask to Buy or a bank confirmation: nothing is owned until a transaction update arrives.</summary>
    Pending,
    Cancelled,
    Failed
}

public sealed record PurchaseOutcome(PurchaseResultKind Kind, StoreEntitlement? Entitlement, string? FailureReason)
{
    public static PurchaseOutcome Cancelled { get; } = new(PurchaseResultKind.Cancelled, null, null);
    public static PurchaseOutcome Pending { get; } = new(PurchaseResultKind.Pending, null, null);
    public static PurchaseOutcome Purchased(StoreEntitlement entitlement) => new(PurchaseResultKind.Purchased, entitlement, null);
    public static PurchaseOutcome Failed(string reason) => new(PurchaseResultKind.Failed, null, reason);
}

/// <summary>The store answered with an error, or did not answer. Callers keep their last state.</summary>
public class StoreException : Exception
{
    public StoreException(string message) : base(message) { }
    public StoreException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown by <see cref="UnavailableStoreClient"/>: there is no store on this platform.</summary>
public sealed class StoreUnavailableException : StoreException
{
    public StoreUnavailableException() : base("No app store client is available on this platform.") { }
}
