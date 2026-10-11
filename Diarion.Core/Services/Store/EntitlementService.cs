using System.Text.Json;
using System.Text.Json.Serialization;

namespace Diarion.Services.Store;

public interface IEntitlementService
{
    /// <summary>Valid from construction: restored from the store cache before the first frame.</summary>
    AccessState Current { get; }

    /// <summary>Fires only when the state actually changes, possibly off the UI thread.</summary>
    event Action<AccessState>? Changed;

    /// <summary>Re-derives the state from the store. Never throws for store trouble: without an
    /// answer the last known state stands until its own expiry (spec 15, R1.2).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns what the store says into one <see cref="AccessState"/>, in the order spec 15 R1.3 fixes:
/// legacy first, then live entitlements, then the renewal state, else read-only. The last live
/// answer is cached so a cold start offline reaches the same conclusion the store would.
/// </summary>
public sealed class EntitlementService : IEntitlementService, IDisposable
{
    internal const string CachedEntitlementKey = "access.entitlement";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IStoreClient _storeClient;
    private readonly IEntitlementStore _store;
    private readonly ILegacyAccessDetector _legacy;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Action _onTransactionsChanged;

    public EntitlementService(
        IStoreClient storeClient,
        IEntitlementStore store,
        ILegacyAccessDetector legacy,
        TimeProvider clock)
    {
        _storeClient = storeClient;
        _store = store;
        _legacy = legacy;
        _clock = clock;

        Current = Restore();

        _onTransactionsChanged = () => _ = RefreshAsync();
        _storeClient.TransactionsChanged += _onTransactionsChanged;
    }

    public AccessState Current { get; private set; }

    public event Action<AccessState>? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await _legacy.EvaluateAsync(cancellationToken).ConfigureAwait(false))
            {
                Publish(AccessState.Legacy);
                return;
            }

            if (!_storeClient.IsAvailable)
            {
                Publish(AccessState.StoreUnavailable);
                return;
            }

            IReadOnlyList<StoreEntitlement> entitlements;
            IReadOnlyList<SubscriptionStatus> statuses;
            try
            {
                entitlements = await _storeClient.GetEntitlementsAsync(cancellationToken).ConfigureAwait(false);
                statuses = await _storeClient.GetSubscriptionStatusesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (StoreException)
            {
                // No answer: keep the cached conclusion, but let time pass on it.
                Publish(Evaluate(ReadCache(), Now));
                return;
            }

            var snapshot = CachedEntitlement.From(entitlements, statuses);
            if (snapshot is null)
                _store.Remove(CachedEntitlementKey);
            else
                _store.Set(CachedEntitlementKey, JsonSerializer.Serialize(snapshot, JsonOptions));

            Publish(Evaluate(snapshot, Now));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _storeClient.TransactionsChanged -= _onTransactionsChanged;
        _refreshGate.Dispose();
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private AccessState Restore()
    {
        if (_legacy.ResolveLocally()) return AccessState.Legacy;
        if (!_storeClient.IsAvailable) return AccessState.StoreUnavailable;
        return Evaluate(ReadCache(), Now);
    }

    private CachedEntitlement? ReadCache()
    {
        var json = _store.Get(CachedEntitlementKey);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CachedEntitlement>(json, JsonOptions);
        }
        catch (JsonException)
        {
            _store.Remove(CachedEntitlementKey);
            return null;
        }
    }

    internal static AccessState Evaluate(CachedEntitlement? cached, DateTime now)
    {
        if (cached is null) return AccessState.ReadOnly;
        if (cached.RevokedAt is not null || cached.RenewalState == SubscriptionRenewalState.Revoked)
            return AccessState.ReadOnly with { ExpiresAt = cached.ExpiresAt };

        var tier = StoreCatalog.TierOf(cached.ProductId);
        if (tier == AccessTier.None) return AccessState.ReadOnly;

        if (cached.ExpiresAt is null || cached.ExpiresAt > now)
        {
            var source = cached.OfferType == StoreOfferType.Introductory ? AccessSource.Trial : AccessSource.Subscription;
            return new AccessState(true, tier, source, cached.ExpiresAt, cached.WillAutoRenew);
        }

        if (cached.RenewalState == SubscriptionRenewalState.InGracePeriod
            && (cached.GracePeriodExpiresAt is null || cached.GracePeriodExpiresAt > now))
        {
            return new AccessState(true, tier, AccessSource.GracePeriod, cached.GracePeriodExpiresAt, cached.WillAutoRenew);
        }

        return AccessState.ReadOnly with { ExpiresAt = cached.ExpiresAt };
    }

    private void Publish(AccessState state)
    {
        if (state == Current) return;
        Current = state;
        Changed?.Invoke(state);
    }

    /// <summary>
    /// The conclusion worth keeping from one store answer: the single entitlement that decides the
    /// state, plus the renewal facts about it. No identifiers (spec 15, R0.3).
    /// </summary>
    internal sealed record CachedEntitlement(
        string ProductId,
        DateTime? ExpiresAt,
        StoreOfferType OfferType,
        DateTime? RevokedAt,
        SubscriptionRenewalState RenewalState,
        DateTime? GracePeriodExpiresAt,
        bool WillAutoRenew)
    {
        public static CachedEntitlement? From(IReadOnlyList<StoreEntitlement> entitlements, IReadOnlyList<SubscriptionStatus> statuses)
        {
            // Highest tier wins, then the one that lasts longest; a revoked transaction only counts
            // when nothing live is left, so that a refund of last year does not hide this year's renewal.
            var best = entitlements
                .Where(e => StoreCatalog.TierOf(e.ProductId) != AccessTier.None)
                .OrderBy(e => e.RevokedAt is null ? 0 : 1)
                .ThenByDescending(e => StoreCatalog.TierOf(e.ProductId))
                .ThenByDescending(e => e.ExpiresAt ?? DateTime.MaxValue)
                .FirstOrDefault();
            if (best is null) return null;

            var status = statuses.FirstOrDefault(s => s.ProductId == best.ProductId) ?? statuses.FirstOrDefault();

            return new CachedEntitlement(
                best.ProductId,
                best.ExpiresAt,
                best.OfferType,
                best.RevokedAt,
                status?.State ?? SubscriptionRenewalState.Unknown,
                status?.GracePeriodExpiresAt,
                status?.WillAutoRenew ?? false);
        }
    }
}
