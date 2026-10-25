using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Store;
using Diarion.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Spec 15, R4 and AC8. Strings are compared through AppResources rather than literals, so the
/// tests hold in either UI language; the Ukrainian plural forms get their own pinned-culture test.
/// </summary>
public class PaywallViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeStoreClient _store = new();
    private readonly InMemoryEntitlementStore _keychain = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly Mock<IExternalLinkService> _links = new();
    private readonly EntitlementService _entitlements;

    public PaywallViewModelTests()
    {
        _entitlements = new EntitlementService(_store, _keychain, new FakeLegacyAccessDetector(), _clock);
    }

    private PaywallViewModel Create() => new(_store, _entitlements, _links.Object);

    private static StoreProduct Product(string id, decimal price, SubscriptionPeriodUnit unit, bool eligible = true, bool trial = true) =>
        new(id, id, "$" + price.ToString(CultureInfo.InvariantCulture), price, "USD",
            new SubscriptionPeriod(unit, 1),
            trial ? new StoreIntroOffer(StoreIntroOfferKind.FreeTrial, new SubscriptionPeriod(SubscriptionPeriodUnit.Day, 3), 1, 0m, "$0") : null,
            eligible);

    /// <summary>The three durations as the owner priced them on 2026-10-25.</summary>
    private void OfferAllPlans(bool eligible = true)
    {
        _store.Products.Add(Product(StoreCatalog.BaseMonthly, 1.99m, SubscriptionPeriodUnit.Month, eligible));
        _store.Products.Add(Product(StoreCatalog.BaseQuarterly, 4.99m, SubscriptionPeriodUnit.Month, eligible));
        _store.Products.Add(Product(StoreCatalog.BaseYearly, 11.99m, SubscriptionPeriodUnit.Year, eligible));
    }

    private void GrantOnPurchase()
    {
        _store.Entitlements.Add(new StoreEntitlement(StoreCatalog.BaseYearly, Now.AddDays(3), StoreOfferType.Introductory, StoreEnvironment.Sandbox, null));
        _store.Statuses.Add(new SubscriptionStatus(StoreCatalog.BaseYearly, SubscriptionRenewalState.Subscribed, true, null));
    }

    [Fact]
    public async Task The_year_comes_first_and_is_preselected_then_three_months_then_the_month()
    {
        OfferAllPlans();
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Plans.Select(p => p.ProductId).Should().Equal(StoreCatalog.BaseYearly, StoreCatalog.BaseQuarterly, StoreCatalog.BaseMonthly);
        vm.SelectedPlan!.ProductId.Should().Be(StoreCatalog.BaseYearly);
        vm.Plans[0].IsSelected.Should().BeTrue();
        vm.Plans[1].IsSelected.Should().BeFalse();
        vm.Plans[2].IsSelected.Should().BeFalse();
        vm.HasPlans.Should().BeTrue();
    }

    [Fact]
    public async Task Prices_come_from_the_store_with_the_period_appended()
    {
        OfferAllPlans();
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Plans[0].PriceText.Should().Be(string.Format(AppResources.PaywallPricePerYearFormat, "$11.99"));
        vm.Plans[1].PriceText.Should().Be(string.Format(AppResources.PaywallPricePerQuarterFormat, "$4.99"));
        vm.Plans[2].PriceText.Should().Be(string.Format(AppResources.PaywallPricePerMonthFormat, "$1.99"));
    }

    [Fact]
    public async Task The_year_and_the_three_months_carry_their_discounts_and_the_month_none()
    {
        OfferAllPlans();
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Plans[0].Badge.Should().Be(string.Format(AppResources.PaywallDiscountFormat, 50));
        vm.Plans[1].Badge.Should().Be(string.Format(AppResources.PaywallDiscountFormat, 16));
        vm.Plans[2].HasBadge.Should().BeFalse();
    }

    [Theory]
    [InlineData(89, 499, 12, 53)]
    [InlineData(1.99, 11.99, 12, 50)]
    [InlineData(1.99, 4.99, 3, 16)]
    public void The_discount_is_computed_from_the_prices(decimal monthly, decimal longer, int months, int expectedPercent)
    {
        var badge = PaywallViewModel.DiscountBadge(
            Product(StoreCatalog.BaseMonthly, monthly, SubscriptionPeriodUnit.Month),
            Product(StoreCatalog.BaseYearly, longer, SubscriptionPeriodUnit.Year),
            months);

        badge.Should().Be(string.Format(AppResources.PaywallDiscountFormat, expectedPercent));
    }

    [Fact]
    public void No_discount_is_shown_when_the_longer_plan_is_not_cheaper_or_a_price_is_missing()
    {
        var monthly = Product(StoreCatalog.BaseMonthly, 1m, SubscriptionPeriodUnit.Month);
        var expensiveYear = Product(StoreCatalog.BaseYearly, 12m, SubscriptionPeriodUnit.Year);

        PaywallViewModel.DiscountBadge(monthly, expensiveYear, 12).Should().BeNull();
        PaywallViewModel.DiscountBadge(null, expensiveYear, 12).Should().BeNull();
        PaywallViewModel.DiscountBadge(monthly, null, 12).Should().BeNull();
    }

    [Fact]
    public async Task An_eligible_user_sees_the_trial_and_the_start_trial_button()
    {
        OfferAllPlans();
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Plans.Should().OnlyContain(p => p.HasTrial);
        vm.Plans[0].TrialText.Should().Be(string.Format(
            AppResources.PaywallTrialFormat,
            PaywallViewModel.Duration(new SubscriptionPeriod(SubscriptionPeriodUnit.Day, 3), 1),
            vm.Plans[0].PriceText));
        vm.PurchaseButtonText.Should().Be(AppResources.PaywallStartTrialButton);
    }

    [Fact]
    public async Task A_user_who_used_the_trial_is_never_told_it_is_free()
    {
        OfferAllPlans(eligible: false);
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Plans.Should().OnlyContain(p => !p.HasTrial);
        vm.PurchaseButtonText.Should().Be(AppResources.PaywallSubscribeButton);
    }

    [Theory]
    [InlineData(SubscriptionPeriodUnit.Week, 2, 1, "14 днів")]
    [InlineData(SubscriptionPeriodUnit.Week, 1, 1, "7 днів")]
    [InlineData(SubscriptionPeriodUnit.Day, 3, 1, "3 дні")]
    [InlineData(SubscriptionPeriodUnit.Day, 1, 1, "1 день")]
    [InlineData(SubscriptionPeriodUnit.Day, 21, 1, "21 день")]
    [InlineData(SubscriptionPeriodUnit.Day, 12, 1, "12 днів")]
    [InlineData(SubscriptionPeriodUnit.Day, 22, 1, "22 дні")]
    [InlineData(SubscriptionPeriodUnit.Month, 1, 1, "1 міс.")]
    public void Trial_lengths_read_correctly_in_ukrainian(SubscriptionPeriodUnit unit, int count, int periods, string expected)
    {
        var previous = AppResources.Culture;
        AppResources.Culture = new CultureInfo("uk-UA");
        try
        {
            PaywallViewModel.Duration(new SubscriptionPeriod(unit, count), periods).Should().Be(expected);
        }
        finally
        {
            AppResources.Culture = previous;
        }
    }

    [Fact]
    public async Task Selecting_a_plan_moves_the_selection()
    {
        OfferAllPlans();
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectPlanCommand.Execute(vm.Plans[2]);

        vm.SelectedPlan!.ProductId.Should().Be(StoreCatalog.BaseMonthly);
        vm.Plans[0].IsSelected.Should().BeFalse();
        vm.Plans[1].IsSelected.Should().BeFalse();
        vm.Plans[2].IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task A_completed_purchase_refreshes_access_and_closes()
    {
        OfferAllPlans();
        var vm = Create();
        var closed = 0;
        vm.Closed = () => closed++;
        await vm.LoadCommand.ExecuteAsync(null);

        GrantOnPurchase();
        _store.NextPurchaseOutcome = PurchaseOutcome.Purchased(_store.Entitlements[0]);
        await vm.PurchaseCommand.ExecuteAsync(null);

        _store.PurchasedProductIds.Should().Equal(StoreCatalog.BaseYearly);
        _entitlements.Current.CanWrite.Should().BeTrue();
        closed.Should().Be(1, "closing twice would pop a page that is not the paywall");
    }

    [Fact]
    public async Task A_pending_purchase_stays_open_and_says_so()
    {
        OfferAllPlans();
        var vm = Create();
        var closed = false;
        vm.Closed = () => closed = true;
        await vm.LoadCommand.ExecuteAsync(null);

        _store.NextPurchaseOutcome = PurchaseOutcome.Pending;
        await vm.PurchaseCommand.ExecuteAsync(null);

        closed.Should().BeFalse();
        vm.Message.Should().Be(AppResources.PaywallPendingMessage);
        _entitlements.Current.CanWrite.Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_purchase_changes_nothing()
    {
        OfferAllPlans();
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        _store.NextPurchaseOutcome = PurchaseOutcome.Cancelled;
        await vm.PurchaseCommand.ExecuteAsync(null);

        vm.HasMessage.Should().BeFalse();
        vm.IsWorking.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_purchase_names_the_reason()
    {
        OfferAllPlans();
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        _store.NextPurchaseOutcome = PurchaseOutcome.Failed("card declined");
        await vm.PurchaseCommand.ExecuteAsync(null);

        vm.Message.Should().Be(string.Format(AppResources.PaywallPurchaseFailedFormat, "card declined"));
    }

    [Fact]
    public async Task Restore_with_a_subscription_closes()
    {
        var vm = Create();
        var closed = false;
        vm.Closed = () => closed = true;
        GrantOnPurchase();

        await vm.RestoreCommand.ExecuteAsync(null);

        _store.SyncCalls.Should().Be(1);
        closed.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_without_a_subscription_says_nothing_was_found()
    {
        var vm = Create();
        var closed = false;
        vm.Closed = () => closed = true;

        await vm.RestoreCommand.ExecuteAsync(null);

        closed.Should().BeFalse();
        vm.Message.Should().Be(AppResources.PaywallRestoreNothingFound);
    }

    [Fact]
    public async Task An_unreachable_store_leaves_a_message_and_a_way_out()
    {
        _store.Failure = new StoreException("offline");
        var vm = Create();
        var closed = false;
        vm.Closed = () => closed = true;

        await vm.LoadCommand.ExecuteAsync(null);

        vm.HasPlans.Should().BeFalse();
        vm.Message.Should().Be(AppResources.PaywallStoreUnavailable);
        vm.PurchaseCommand.CanExecute(null).Should().BeFalse();

        vm.LaterCommand.Execute(null);
        closed.Should().BeTrue("'Not now' always leads into the app (R4.3)");
    }

    [Fact]
    public async Task A_platform_without_a_store_never_asks_it()
    {
        _store.IsAvailable = false;
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        vm.Message.Should().Be(AppResources.PaywallStoreUnavailable);
    }

    [Fact]
    public async Task Access_granted_elsewhere_closes_the_sheet()
    {
        OfferAllPlans();
        var vm = Create();
        var closed = false;
        vm.Closed = () => closed = true;
        await vm.LoadCommand.ExecuteAsync(null);

        GrantOnPurchase();
        await _entitlements.RefreshAsync();

        closed.Should().BeTrue("an Ask-to-Buy approval arrives as a transaction update, not as a purchase result");
    }

    [Fact]
    public async Task The_terms_and_privacy_links_open_the_published_pages()
    {
        var opened = new List<Uri>();
        _links.Setup(l => l.OpenAsync(It.IsAny<Uri>())).Callback<Uri>(opened.Add).Returns(Task.CompletedTask);
        var vm = Create();

        await vm.OpenTermsCommand.ExecuteAsync(null);
        await vm.OpenPrivacyCommand.ExecuteAsync(null);

        opened.Should().HaveCount(2);
        opened[0].AbsoluteUri.Should().MatchRegex(@"^https://diarion\.org/(en/)?terms$");
        opened[1].AbsoluteUri.Should().MatchRegex(@"^https://diarion\.org/(en/)?privacy$");
    }

    [Theory]
    [InlineData("uk-UA", "https://diarion.org/terms")]
    [InlineData("en-US", "https://diarion.org/en/terms")]
    [InlineData("pl-PL", "https://diarion.org/en/terms")]
    public void Links_follow_the_app_language(string culture, string expected)
    {
        StoreLinks.Terms(new CultureInfo(culture)).AbsoluteUri.Should().Be(expected);
    }
}
