namespace Diarion.Services.Store;

/// <summary>
/// The app's whole view of the platform store. On iOS this is the StoreKit 2 facade; elsewhere it
/// is <see cref="UnavailableStoreClient"/>. Every method may throw <see cref="StoreException"/>;
/// none of them ever blocks on the network silently — callers decide what a missing answer means.
/// </summary>
public interface IStoreClient
{
    /// <summary>False when nothing here can work. The entitlement service then grants write access
    /// (spec 15, R1.5) rather than locking a platform that has no way to pay.</summary>
    bool IsAvailable { get; }

    /// <summary>Raised when the store reports a transaction outside a purchase call: a renewal, a
    /// refund, an Ask-to-Buy approval. May fire on any thread.</summary>
    event Action? TransactionsChanged;

    Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken = default);

    Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken cancellationToken = default);

    /// <summary>Verified transactions the user is currently entitled to, from the store's local cache
    /// when offline.</summary>
    Task<IReadOnlyList<StoreEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken = default);

    /// <summary>Renewal state for every subscription in the group the catalog knows about.</summary>
    Task<IReadOnlyList<SubscriptionStatus>> GetSubscriptionStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>"Restore Purchases": asks the store to re-sync with the user's account.</summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>The receipt for the app itself; null when the store cannot produce one right now.</summary>
    Task<AppTransactionInfo?> GetAppTransactionAsync(CancellationToken cancellationToken = default);

    Task ShowManageSubscriptionsAsync(CancellationToken cancellationToken = default);
}
