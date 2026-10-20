namespace Diarion.Services.Store;

/// <summary>
/// The client for platforms without a store yet. It answers "not available" and refuses everything
/// else, so a paywall can never be reached by accident where it could not be paid.
/// </summary>
public sealed class UnavailableStoreClient : IStoreClient
{
    public bool IsAvailable => false;

#pragma warning disable CS0067 // Never raised: there is no store to raise it.
    public event Action? TransactionsChanged;
#pragma warning restore CS0067

    public Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken = default) =>
        throw new StoreUnavailableException();

    public Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken cancellationToken = default) =>
        throw new StoreUnavailableException();

    public Task<IReadOnlyList<StoreEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken = default) =>
        throw new StoreUnavailableException();

    public Task<IReadOnlyList<SubscriptionStatus>> GetSubscriptionStatusesAsync(CancellationToken cancellationToken = default) =>
        throw new StoreUnavailableException();

    public Task SyncAsync(CancellationToken cancellationToken = default) =>
        throw new StoreUnavailableException();

    public Task<AppTransactionInfo?> GetAppTransactionAsync(CancellationToken cancellationToken = default) =>
        throw new StoreUnavailableException();

    public Task ShowManageSubscriptionsAsync(CancellationToken cancellationToken = default) =>
        throw new StoreUnavailableException();
}
