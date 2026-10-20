namespace Diarion.Services.Store;

/// <summary>
/// The product identifiers as created in App Store Connect, and the one number the legacy rule
/// hangs on. Prices are deliberately absent: the store is the only source of a price (spec 15, R0.4).
/// </summary>
public static class StoreCatalog
{
    /// <summary>Reference name of the subscription group; Base and Plus are levels inside it.</summary>
    public const string SubscriptionGroup = "diarion.base";

    public const string BaseMonthly = "diarion.base.monthly";
    public const string BaseYearly = "diarion.base.yearly";

    // Reserved for the AI tier. Not created in App Store Connect yet; listed so the tier mapping
    // and the status query do not need touching when it is.
    public const string PlusMonthly = "diarion.plus.monthly";
    public const string PlusYearly = "diarion.plus.yearly";

    /// <summary>
    /// 1.0 shipped as build 2 (`ApplicationVersion` in Diarion.csproj). Anyone whose
    /// `AppTransaction.originalAppVersion` is this or lower installed before the paywall existed.
    /// </summary>
    public const int LastFreeBuild = 2;

    public static IReadOnlyList<string> BaseProductIds { get; } = [BaseMonthly, BaseYearly];

    public static IReadOnlyList<string> AllProductIds { get; } = [BaseMonthly, BaseYearly, PlusMonthly, PlusYearly];

    public static AccessTier TierOf(string productId) => productId switch
    {
        PlusMonthly or PlusYearly => AccessTier.Plus,
        BaseMonthly or BaseYearly => AccessTier.Base,
        _ => AccessTier.None
    };
}
