namespace Diarion.Services.Store;

/// <summary>What the subscription buys. <see cref="None"/> is also the tier of a legacy user: they
/// write for free, but a paid tier above Base (AI) is not part of that promise.</summary>
public enum AccessTier
{
    None,
    Base,
    Plus
}

/// <summary>Why the current <see cref="AccessState"/> is what it is — shown on the subscription
/// screen, never used for gating on its own.</summary>
public enum AccessSource
{
    None,
    /// <summary>Installed build 2 or earlier: everything from 1.0 stays free forever.</summary>
    Legacy,
    Subscription,
    /// <summary>A subscription inside its introductory offer; same entitlement, different wording.</summary>
    Trial,
    /// <summary>Apple is retrying the payment; access is kept until the grace period ends.</summary>
    GracePeriod,
    /// <summary>No store on this platform yet (Android before Play Billing, Windows, Mac Catalyst).</summary>
    StoreUnavailable
}

/// <summary>
/// The one answer the rest of the app asks: may the user write? Everything else here is for the
/// subscription screen. Read paths never consult this type — spec 15, R0.1.
/// </summary>
public sealed record AccessState(
    bool CanWrite,
    AccessTier Tier,
    AccessSource Source,
    DateTime? ExpiresAt,
    bool WillAutoRenew)
{
    public bool IsReadOnly => !CanWrite;

    /// <summary>AI is a Plus feature for everyone, legacy users included (spec 15, decision 4).</summary>
    public bool HasAi => Tier == AccessTier.Plus;

    public static AccessState ReadOnly { get; } = new(false, AccessTier.None, AccessSource.None, null, false);

    public static AccessState Legacy { get; } = new(true, AccessTier.None, AccessSource.Legacy, null, false);

    public static AccessState StoreUnavailable { get; } = new(true, AccessTier.None, AccessSource.StoreUnavailable, null, false);
}
