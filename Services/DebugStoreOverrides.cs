#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Services.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Diarion.Services;

/// <summary>
/// A pretend App Store for working on the paywall and read-only mode where no store exists — on
/// Windows, in practice. <c>DIARION_FAKE_STORE=new</c> plays a new user with a fresh trial on
/// offer; <c>DIARION_FAKE_STORE=legacy</c> plays a 1.0 user. State lives in memory and is gone on
/// the next launch, so the real keychain never learns anything from it. Debug builds only: the
/// whole file is compiled out of Release, the same rule as the mock-data seeders (AGENTS.md).
/// </summary>
internal static class DebugStoreOverrides
{
    public static void Apply(IServiceCollection services)
    {
        var mode = Environment.GetEnvironmentVariable("DIARION_FAKE_STORE");
        if (mode is not ("new" or "legacy")) return;

        // Registered after the real ones: the last registration is the one DI hands out.
        services.AddSingleton<IStoreClient, DebugStoreClient>();
        services.AddSingleton<IEntitlementStore, MemoryEntitlementStore>();
        services.AddSingleton<IInstallEvidence>(new DebugInstallEvidence(mode == "legacy"));
    }

    private sealed class DebugInstallEvidence : IInstallEvidence
    {
        public DebugInstallEvidence(bool legacy)
        {
            IsFirstLaunchOfPaidEra = legacy;
            OnboardingCompletedBeforeThisLaunch = legacy;
        }

        public bool IsFirstLaunchOfPaidEra { get; }
        public bool OnboardingCompletedBeforeThisLaunch { get; }
    }

    private sealed class MemoryEntitlementStore : IEntitlementStore
    {
        private readonly Dictionary<string, string> _values = new();
        public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => _values[key] = value;
        public void Remove(string key) => _values.Remove(key);
    }

    private sealed class DebugStoreClient : IStoreClient
    {
        private static readonly StoreIntroOffer Trial =
            new(StoreIntroOfferKind.FreeTrial, new SubscriptionPeriod(SubscriptionPeriodUnit.Week, 2), 1, 0m, "0 ₴");

        private readonly List<StoreEntitlement> _owned = new();
        private bool _trialUsed;

        public bool IsAvailable => true;

        public event Action? TransactionsChanged;

        public async Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken = default)
        {
            await Task.Delay(400, cancellationToken); // long enough to see the spinner
            var all = new[]
            {
                new StoreProduct(StoreCatalog.BaseMonthly, "Diarion Base", "89,00 ₴", 89m, "UAH",
                    new SubscriptionPeriod(SubscriptionPeriodUnit.Month, 1), Trial, !_trialUsed),
                new StoreProduct(StoreCatalog.BaseYearly, "Diarion Base", "499,00 ₴", 499m, "UAH",
                    new SubscriptionPeriod(SubscriptionPeriodUnit.Year, 1), Trial, !_trialUsed)
            };
            return all.Where(p => productIds.Contains(p.Id)).ToList();
        }

        public async Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
        {
            await Task.Delay(600, cancellationToken);
            var entitlement = new StoreEntitlement(productId, DateTime.UtcNow.AddDays(14),
                _trialUsed ? StoreOfferType.None : StoreOfferType.Introductory, StoreEnvironment.Xcode, null);
            _trialUsed = true;
            _owned.Clear();
            _owned.Add(entitlement);
            TransactionsChanged?.Invoke();
            return PurchaseOutcome.Purchased(entitlement);
        }

        public Task<IReadOnlyList<StoreEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoreEntitlement>>(_owned.ToList());

        public Task<IReadOnlyList<SubscriptionStatus>> GetSubscriptionStatusesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionStatus>>(_owned
                .Select(e => new SubscriptionStatus(e.ProductId, SubscriptionRenewalState.Subscribed, true, null))
                .ToList());

        public Task SyncAsync(CancellationToken cancellationToken = default) => Task.Delay(400, cancellationToken);

        public Task<AppTransactionInfo?> GetAppTransactionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AppTransactionInfo?>(new AppTransactionInfo("3", StoreEnvironment.Xcode, DateTime.UtcNow));

        public Task ShowManageSubscriptionsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
#endif
