using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Services.Store;

namespace Diarion.Tests;

/// <summary>
/// A store that answers from lists the test fills in. Every call is counted so a test can say
/// "the store was never asked", which for the legacy and read-only rules matters more than what
/// it answered.
/// </summary>
public sealed class FakeStoreClient : IStoreClient
{
    public bool IsAvailable { get; set; } = true;
    public List<StoreProduct> Products { get; } = new();
    public List<StoreEntitlement> Entitlements { get; } = new();
    public List<SubscriptionStatus> Statuses { get; } = new();
    public AppTransactionInfo? AppTransaction { get; set; }
    public PurchaseOutcome NextPurchaseOutcome { get; set; } = PurchaseOutcome.Cancelled;

    /// <summary>When set, every query throws it — the store is unreachable.</summary>
    public StoreException? Failure { get; set; }

    public int EntitlementCalls { get; private set; }
    public int StatusCalls { get; private set; }
    public int AppTransactionCalls { get; private set; }
    public int SyncCalls { get; private set; }
    public List<string> PurchasedProductIds { get; } = new();

    public event Action? TransactionsChanged;

    public void RaiseTransactionsChanged() => TransactionsChanged?.Invoke();

    public Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<StoreProduct>>(Products.Where(p => productIds.Contains(p.Id)).ToList());
    }

    public Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        PurchasedProductIds.Add(productId);
        return Task.FromResult(NextPurchaseOutcome);
    }

    public Task<IReadOnlyList<StoreEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken = default)
    {
        EntitlementCalls++;
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<StoreEntitlement>>(Entitlements.ToList());
    }

    public Task<IReadOnlyList<SubscriptionStatus>> GetSubscriptionStatusesAsync(CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        ThrowIfFailing();
        return Task.FromResult<IReadOnlyList<SubscriptionStatus>>(Statuses.ToList());
    }

    public Task SyncAsync(CancellationToken cancellationToken = default)
    {
        SyncCalls++;
        ThrowIfFailing();
        return Task.CompletedTask;
    }

    public Task<AppTransactionInfo?> GetAppTransactionAsync(CancellationToken cancellationToken = default)
    {
        AppTransactionCalls++;
        ThrowIfFailing();
        return Task.FromResult(AppTransaction);
    }

    public Task ShowManageSubscriptionsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private void ThrowIfFailing()
    {
        if (Failure is not null) throw Failure;
    }
}

public sealed class InMemoryEntitlementStore : IEntitlementStore
{
    public Dictionary<string, string> Values { get; } = new();

    public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value) => Values[key] = value;

    public void Remove(string key) => Values.Remove(key);
}

public sealed class FakeInstallEvidence : IInstallEvidence
{
    public bool IsFirstLaunchOfPaidEra { get; set; }
    public bool OnboardingCompletedBeforeThisLaunch { get; set; }
}

/// <summary>A clock the test moves by hand; the service only ever asks it for UTC now.</summary>
public sealed class FakeTimeProvider : TimeProvider
{
    public FakeTimeProvider(DateTime utcNow)
    {
        UtcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A detector with a fixed answer, for tests of the services above it.</summary>
public sealed class FakeLegacyAccessDetector : ILegacyAccessDetector
{
    public bool IsKnownLegacy { get; set; }
    public bool EvaluateResult { get; set; }
    public int EvaluateCalls { get; private set; }

    public Task<bool> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        EvaluateCalls++;
        return Task.FromResult(EvaluateResult || IsKnownLegacy);
    }
}

/// <summary>An entitlement service pinned to one state, for guard and service tests.</summary>
public sealed class FixedEntitlementService : IEntitlementService
{
    public FixedEntitlementService(AccessState state)
    {
        Current = state;
    }

    public AccessState Current { get; private set; }

    public event Action<AccessState>? Changed;

    public void Set(AccessState state)
    {
        Current = state;
        Changed?.Invoke(state);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
