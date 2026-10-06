using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Services.Store;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The state table of spec 15, R1.3, one row per test. The store is a fake that answers from
/// lists and counts how often it was asked; "never asked" is a result in its own right here.
/// </summary>
public class EntitlementServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private const string CacheKey = "access.entitlement";

    private readonly FakeStoreClient _client = new();
    private readonly InMemoryEntitlementStore _store = new();
    private readonly FakeLegacyAccessDetector _legacy = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private EntitlementService Create() => new(_client, _store, _legacy, _clock);

    private static StoreEntitlement Entitlement(
        DateTime? expiresAt,
        string productId = StoreCatalog.BaseYearly,
        StoreOfferType offer = StoreOfferType.None,
        DateTime? revokedAt = null) =>
        new(productId, expiresAt, offer, StoreEnvironment.Production, revokedAt);

    private static SubscriptionStatus Status(
        SubscriptionRenewalState state,
        bool willAutoRenew = true,
        DateTime? graceUntil = null,
        string productId = StoreCatalog.BaseYearly) =>
        new(productId, state, willAutoRenew, graceUntil);

    [Fact]
    public void A_fresh_install_with_a_store_is_read_only_until_the_store_answers()
    {
        Create().Current.Should().Be(AccessState.ReadOnly);
    }

    [Fact]
    public async Task A_platform_without_a_store_may_write_and_is_never_asked()
    {
        _client.IsAvailable = false;
        var service = Create();

        service.Current.Should().Be(AccessState.StoreUnavailable);
        await service.RefreshAsync();

        service.Current.Should().Be(AccessState.StoreUnavailable);
        _client.EntitlementCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_known_legacy_user_is_restored_before_the_store_is_asked()
    {
        _legacy.IsKnownLegacy = true;
        var service = Create();

        service.Current.Should().Be(AccessState.Legacy);
        await service.RefreshAsync();

        service.Current.Should().Be(AccessState.Legacy);
        _client.EntitlementCalls.Should().Be(0);
    }

    [Fact]
    public async Task Legacy_wins_over_a_live_subscription_and_the_store_is_not_consulted()
    {
        _legacy.EvaluateResult = true;
        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        var service = Create();

        await service.RefreshAsync();

        service.Current.Should().Be(AccessState.Legacy);
        _client.EntitlementCalls.Should().Be(0);
    }

    [Fact]
    public async Task An_active_yearly_subscription_grants_base_and_is_cached()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        var service = Create();

        await service.RefreshAsync();

        service.Current.Should().Be(new AccessState(true, AccessTier.Base, AccessSource.Subscription, Now.AddDays(30), true));
        _store.Values.Should().ContainKey(CacheKey);
    }

    [Fact]
    public async Task An_introductory_offer_is_reported_as_a_trial()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(14), offer: StoreOfferType.Introductory));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        var service = Create();

        await service.RefreshAsync();

        service.Current.CanWrite.Should().BeTrue();
        service.Current.Source.Should().Be(AccessSource.Trial);
        service.Current.Tier.Should().Be(AccessTier.Base);
    }

    [Fact]
    public async Task A_revoked_entitlement_is_read_only_in_the_same_call()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(30), revokedAt: Now.AddDays(-1)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Revoked));
        var service = Create();

        await service.RefreshAsync();

        service.Current.CanWrite.Should().BeFalse();
        service.Current.Source.Should().Be(AccessSource.None);
    }

    [Fact]
    public async Task A_grace_period_keeps_access_after_the_expiry_date()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(-1)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.InGracePeriod, graceUntil: Now.AddDays(5)));
        var service = Create();

        await service.RefreshAsync();

        service.Current.CanWrite.Should().BeTrue();
        service.Current.Source.Should().Be(AccessSource.GracePeriod);
        service.Current.ExpiresAt.Should().Be(Now.AddDays(5));
    }

    [Fact]
    public async Task Expired_without_a_grace_period_is_read_only()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(-1)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Expired, willAutoRenew: false));
        var service = Create();

        await service.RefreshAsync();

        service.Current.CanWrite.Should().BeFalse();
        service.Current.ExpiresAt.Should().Be(Now.AddDays(-1), "the subscription screen still says when it ended");
    }

    [Fact]
    public async Task A_store_failure_keeps_the_last_state_until_it_expires_by_itself()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(3)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        var service = Create();
        await service.RefreshAsync();

        _client.Failure = new StoreException("offline");
        await service.RefreshAsync();
        service.Current.CanWrite.Should().BeTrue("no answer is not a downgrade");

        _clock.Advance(TimeSpan.FromDays(4));
        await service.RefreshAsync();
        service.Current.CanWrite.Should().BeFalse("but time still passes on the cached state");
    }

    [Fact]
    public async Task The_cached_state_survives_a_cold_start_without_asking_the_store()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        await Create().RefreshAsync();

        var restarted = Create();

        restarted.Current.CanWrite.Should().BeTrue();
        restarted.Current.Source.Should().Be(AccessSource.Subscription);
        _client.EntitlementCalls.Should().Be(1);
    }

    [Fact]
    public async Task The_cached_state_expires_on_a_cold_start_too()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(3)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        await Create().RefreshAsync();

        _clock.Advance(TimeSpan.FromDays(4));

        Create().Current.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task No_entitlements_clears_the_cache()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        var service = Create();
        await service.RefreshAsync();

        _client.Entitlements.Clear();
        _client.Statuses.Clear();
        await service.RefreshAsync();

        service.Current.Should().Be(AccessState.ReadOnly);
        _store.Values.Should().NotContainKey(CacheKey);
    }

    [Fact]
    public void A_corrupt_cache_is_ignored_not_fatal()
    {
        _store.Set(CacheKey, "not json");

        Create().Current.Should().Be(AccessState.ReadOnly);
    }

    [Fact]
    public async Task The_highest_tier_wins_when_both_are_present()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(30), StoreCatalog.BaseMonthly));
        _client.Entitlements.Add(Entitlement(Now.AddDays(10), StoreCatalog.PlusMonthly));
        var service = Create();

        await service.RefreshAsync();

        service.Current.Tier.Should().Be(AccessTier.Plus);
        service.Current.HasAi.Should().BeTrue();
    }

    [Fact]
    public async Task An_old_refund_does_not_hide_a_live_renewal()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(-300), revokedAt: Now.AddDays(-310)));
        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        var service = Create();

        await service.RefreshAsync();

        service.Current.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void Only_plus_has_ai()
    {
        AccessState.Legacy.HasAi.Should().BeFalse("1.0 users keep 1.0, not the AI tier");
        new AccessState(true, AccessTier.Base, AccessSource.Subscription, null, true).HasAi.Should().BeFalse();
        new AccessState(true, AccessTier.Plus, AccessSource.Subscription, null, true).HasAi.Should().BeTrue();
    }

    [Fact]
    public async Task Changed_fires_only_when_the_state_differs()
    {
        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        var service = Create();
        var changes = new List<AccessState>();
        service.Changed += changes.Add;

        await service.RefreshAsync();
        await service.RefreshAsync();

        changes.Should().ContainSingle().Which.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void A_transaction_update_from_the_store_triggers_a_refresh()
    {
        var service = Create();
        service.Current.CanWrite.Should().BeFalse();

        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        _client.Statuses.Add(Status(SubscriptionRenewalState.Subscribed));
        _client.RaiseTransactionsChanged();

        SpinWait.SpinUntil(() => service.Current.CanWrite, TimeSpan.FromSeconds(2)).Should().BeTrue();
    }

    [Fact]
    public async Task Disposing_unsubscribes_from_the_store()
    {
        var service = Create();
        service.Dispose();

        _client.Entitlements.Add(Entitlement(Now.AddDays(30)));
        _client.RaiseTransactionsChanged();
        await Task.Delay(50);

        service.Current.CanWrite.Should().BeFalse();
    }
}
