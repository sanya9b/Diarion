using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Store;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>Runs dispatched work inline, so a state change is visible on the next line.</summary>
internal sealed class InlineDispatcher : IDispatcherService
{
    public void InvokeOnMainThread(Action action) => action();
}

/// <summary>Collects paywall requests from the messenger for the life of one test.</summary>
internal sealed class PaywallRequests : IDisposable
{
    public List<PaywallReason> Reasons { get; } = new();

    public PaywallRequests() =>
        WeakReferenceMessenger.Default.Register<PaywallRequests, PaywallRequestedMessage>(this, (r, m) => r.Reasons.Add(m.Reason));

    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}

public class SubscriptionSectionViewModelTests
{
    private static readonly DateTime Until = new(2027, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeStoreClient _store = new();

    private SubscriptionSectionViewModel Create(AccessState state, out FixedEntitlementService entitlements)
    {
        entitlements = new FixedEntitlementService(state);
        return new SubscriptionSectionViewModel(entitlements, _store, new InlineDispatcher());
    }

    [Fact]
    public void A_legacy_user_reads_the_promise_and_may_support_but_has_nothing_to_manage()
    {
        var vm = Create(AccessState.Legacy, out _);

        vm.StatusText.Should().Be(AppResources.SubscriptionStatusLegacy);
        vm.CanSupport.Should().BeTrue();
        vm.CanManage.Should().BeFalse();
        vm.CanSubscribe.Should().BeFalse();
        vm.CanRestore.Should().BeFalse("there is nothing to restore");
    }

    [Fact]
    public async Task Restore_that_finds_a_subscription_updates_the_card_and_says_nothing_more()
    {
        var vm = Create(AccessState.ReadOnly, out var entitlements);
        vm.CanRestore.Should().BeTrue();
        entitlements.AfterRefresh = new AccessState(true, AccessTier.Base, AccessSource.Subscription, Until, true);

        await vm.RestoreCommand.ExecuteAsync(null);

        _store.SyncCalls.Should().Be(1);
        vm.CanSubscribe.Should().BeFalse();
        vm.CanManage.Should().BeTrue();
        vm.HasRestoreMessage.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_that_finds_nothing_says_so()
    {
        var vm = Create(AccessState.ReadOnly, out _);

        await vm.RestoreCommand.ExecuteAsync(null);

        vm.RestoreMessage.Should().Be(AppResources.PaywallRestoreNothingFound);
        vm.HasRestoreMessage.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_without_the_store_says_the_store_is_unreachable()
    {
        _store.Failure = new StoreException("offline");
        var vm = Create(AccessState.ReadOnly, out _);

        await vm.RestoreCommand.ExecuteAsync(null);

        vm.RestoreMessage.Should().Be(AppResources.PaywallStoreUnavailable);
        vm.IsRestoring.Should().BeFalse();
    }

    [Fact]
    public void A_trial_names_its_end_and_can_be_managed()
    {
        var vm = Create(new AccessState(true, AccessTier.Base, AccessSource.Trial, Until, true), out _);

        vm.StatusText.Should().StartWith(AppResources.SubscriptionStatusTrialFormat.Split('{')[0]);
        vm.StatusText.Should().Contain("2027");
        vm.CanManage.Should().BeTrue();
        vm.CanSubscribe.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_subscription_says_whether_it_renews(bool willAutoRenew)
    {
        var vm = Create(new AccessState(true, AccessTier.Base, AccessSource.Subscription, Until, willAutoRenew), out _);

        var format = willAutoRenew ? AppResources.SubscriptionStatusRenewsFormat : AppResources.SubscriptionStatusEndsFormat;
        vm.StatusText.Should().StartWith(format.Split('{')[0]);
    }

    [Fact]
    public void Read_only_offers_the_paywall_and_nothing_else()
    {
        var vm = Create(AccessState.ReadOnly, out _);

        vm.StatusText.Should().Be(AppResources.SubscriptionStatusReadOnly);
        vm.CanSubscribe.Should().BeTrue();
        vm.CanManage.Should().BeFalse();
        vm.CanSupport.Should().BeFalse();
    }

    [Fact]
    public void The_card_is_hidden_where_there_is_no_store()
    {
        _store.IsAvailable = false;

        Create(AccessState.StoreUnavailable, out _).IsVisible.Should().BeFalse();
    }

    [Fact]
    public void The_card_follows_the_state_as_it_changes()
    {
        var vm = Create(AccessState.ReadOnly, out var entitlements);

        entitlements.Set(new AccessState(true, AccessTier.Base, AccessSource.Subscription, Until, true));

        vm.CanSubscribe.Should().BeFalse();
        vm.CanManage.Should().BeTrue();
    }

    [Fact]
    public void Subscribe_and_support_ask_the_shell_for_the_paywall()
    {
        using var requests = new PaywallRequests();
        var vm = Create(AccessState.ReadOnly, out _);

        vm.SubscribeCommand.Execute(null);

        requests.Reasons.Should().Equal(PaywallReason.UserRequested);
    }

    [Fact]
    public async Task A_failing_manage_sheet_does_not_throw()
    {
        var store = new Mock<IStoreClient>();
        store.SetupGet(s => s.IsAvailable).Returns(true);
        store.Setup(s => s.ShowManageSubscriptionsAsync(default)).ThrowsAsync(new StoreException("no scene"));
        var vm = new SubscriptionSectionViewModel(new FixedEntitlementService(AccessState.Legacy), store.Object, new InlineDispatcher());

        await vm.Invoking(v => v.ManageCommand.ExecuteAsync(null)).Should().NotThrowAsync();
    }
}

public class AccessBannerViewModelTests
{
    private readonly InMemoryEntitlementStore _keychain = new();

    private AccessBannerViewModel Create(AccessState state, out FixedEntitlementService entitlements)
    {
        entitlements = new FixedEntitlementService(state);
        return new AccessBannerViewModel(entitlements, _keychain, new InlineDispatcher());
    }

    [Fact]
    public void Read_only_shows_the_banner_and_no_legacy_card()
    {
        var vm = Create(AccessState.ReadOnly, out _);

        vm.IsReadOnly.Should().BeTrue();
        vm.ShowLegacyCard.Should().BeFalse();
    }

    [Fact]
    public void A_legacy_user_sees_the_card_until_dismissing_it_and_never_again()
    {
        var vm = Create(AccessState.Legacy, out _);
        vm.IsReadOnly.Should().BeFalse();
        vm.ShowLegacyCard.Should().BeTrue();

        vm.DismissLegacyCardCommand.Execute(null);

        vm.ShowLegacyCard.Should().BeFalse();
        Create(AccessState.Legacy, out _).ShowLegacyCard.Should().BeFalse("the dismissal is persisted");
    }

    [Fact]
    public void A_subscriber_sees_neither()
    {
        var vm = Create(new AccessState(true, AccessTier.Base, AccessSource.Subscription, null, true), out _);

        vm.IsReadOnly.Should().BeFalse();
        vm.ShowLegacyCard.Should().BeFalse();
    }

    [Fact]
    public void The_banner_appears_when_the_subscription_lapses_while_open()
    {
        var vm = Create(new AccessState(true, AccessTier.Base, AccessSource.Subscription, null, true), out var entitlements);

        entitlements.Set(AccessState.ReadOnly);

        vm.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Someone_who_never_subscribed_is_invited_not_told_it_ended()
    {
        var vm = Create(AccessState.ReadOnly, out _);

        vm.BannerText.Should().Be(AppResources.ReadOnlyBannerNewText);
        vm.BannerButtonText.Should().Be(AppResources.ReadOnlyBannerNewButton);
    }

    [Fact]
    public void Someone_whose_subscription_lapsed_is_told_so_and_offered_renewal()
    {
        _keychain.Set(EntitlementService.HadSubscriptionKey, "1");

        var vm = Create(AccessState.ReadOnly, out _);

        vm.BannerText.Should().Be(AppResources.ReadOnlyBannerText);
        vm.BannerButtonText.Should().Be(AppResources.ReadOnlyBannerButton);
    }

    [Fact]
    public void Renew_asks_the_shell_for_the_paywall()
    {
        using var requests = new PaywallRequests();
        var vm = Create(AccessState.ReadOnly, out _);

        vm.RenewCommand.Execute(null);

        requests.Reasons.Should().Equal(PaywallReason.UserRequested);
    }
}

public class WriteGateTests
{
    [Fact]
    public async Task A_permitted_write_runs_and_reports_true()
    {
        using var requests = new PaywallRequests();
        var ran = false;

        (await WriteGate.TryAsync(() => { ran = true; return Task.CompletedTask; })).Should().BeTrue();

        ran.Should().BeTrue();
        requests.Reasons.Should().BeEmpty();
    }

    [Fact]
    public async Task A_blocked_write_reports_false_and_asks_for_the_paywall_once()
    {
        using var requests = new PaywallRequests();

        (await WriteGate.TryAsync(() => throw new ReadOnlyAccessException())).Should().BeFalse();

        requests.Reasons.Should().Equal(PaywallReason.WriteBlocked);
    }

    [Fact]
    public async Task Any_other_failure_still_surfaces()
    {
        await FluentActions.Awaiting(() => WriteGate.TryAsync(() => throw new InvalidOperationException("disk full")))
            .Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_view_model_stops_at_a_blocked_write_instead_of_showing_an_unsaved_change()
    {
        // The real path end to end: a read-only guard in a real service, called by a real view model.
        using var requests = new PaywallRequests();
        using var db = new Diarion.Services.Database.DatabaseContext(useInMemory: true);
        var guard = new AccessGuard(new FixedEntitlementService(AccessState.ReadOnly));
        var wishlist = new WishlistService(db, guard);
        var vm = new WishlistViewModel(wishlist, Mock.Of<IDialogService>());

        await vm.LoadAsync();
        vm.OpenAddFormCommand.Execute(null);
        vm.NewWantText = "A book";
        await vm.SaveEntryCommand.ExecuteAsync(null);

        vm.Entries.Should().BeEmpty("nothing was saved, so nothing may appear");
        vm.IsAddFormVisible.Should().BeTrue("the form stays open with the text in it, not silently discarded");
        requests.Reasons.Should().Equal(PaywallReason.WriteBlocked);
    }
}
