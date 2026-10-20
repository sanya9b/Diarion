using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels;

/// <summary>One plan card on the paywall. Every string on it is built from what the store said.</summary>
public partial class PaywallPlan : ObservableObject
{
    public PaywallPlan(string productId, string title, string priceText, string? badge, string? trialText)
    {
        ProductId = productId;
        Title = title;
        PriceText = priceText;
        Badge = badge;
        TrialText = trialText;
    }

    public string ProductId { get; }
    public string Title { get; }

    /// <summary>"$11.99 / year" — the store's own formatted price plus the period.</summary>
    public string PriceText { get; }

    /// <summary>"Save 53%" on the yearly card; computed from the two prices, never typed in.</summary>
    public string? Badge { get; }
    public bool HasBadge => Badge is not null;

    /// <summary>"14 days free, then $11.99 / year"; null when this Apple ID has used its trial.</summary>
    public string? TrialText { get; }
    public bool HasTrial => TrialText is not null;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The paywall (spec 15, R4). Built to Apple 3.1.2: the price per period, what happens after the
/// trial, how to cancel, Restore, and working links to the terms and the privacy policy. It can
/// always be left — "Not now" leads into read-only mode, never into a dead end (R4.3).
/// </summary>
public partial class PaywallViewModel : BaseViewModel
{
    private readonly IStoreClient _store;
    private readonly IEntitlementService _entitlements;
    private readonly IExternalLinkService _links;
    private bool _closed;

    public PaywallViewModel(IStoreClient store, IEntitlementService entitlements, IExternalLinkService links)
    {
        _store = store;
        _entitlements = entitlements;
        _links = links;
    }

    /// <summary>Set by the page: dismiss the sheet. Called at most once.</summary>
    public Action? Closed { get; set; }

    public ObservableCollection<PaywallPlan> Plans { get; } = new();

    public bool HasPlans => Plans.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PurchaseButtonText))]
    [NotifyCanExecuteChangedFor(nameof(PurchaseCommand))]
    private PaywallPlan? _selectedPlan;

    /// <summary>The one line under the buttons: pending, failed, nothing to restore, store down.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PurchaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isWorking;

    public string PurchaseButtonText => SelectedPlan?.HasTrial == true
        ? AppResources.PaywallStartTrialButton
        : AppResources.PaywallSubscribeButton;

    private static CultureInfo Culture => AppResources.Culture ?? CultureInfo.CurrentUICulture;

    [RelayCommand]
    private async Task LoadAsync()
    {
        _entitlements.Changed -= OnEntitlementsChanged;
        _entitlements.Changed += OnEntitlementsChanged;

        if (!_store.IsAvailable)
        {
            Message = AppResources.PaywallStoreUnavailable;
            return;
        }

        IsWorking = true;
        try
        {
            var products = await _store.LoadProductsAsync(StoreCatalog.BaseProductIds);
            BuildPlans(products);
            if (!HasPlans) Message = AppResources.PaywallStoreUnavailable;
        }
        catch (StoreException)
        {
            Message = AppResources.PaywallStoreUnavailable;
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private void SelectPlan(PaywallPlan? plan)
    {
        if (plan is null) return;
        foreach (var p in Plans) p.IsSelected = ReferenceEquals(p, plan);
        SelectedPlan = plan;
    }

    private bool CanPurchase() => SelectedPlan is not null && !IsWorking;

    [RelayCommand(CanExecute = nameof(CanPurchase))]
    private async Task PurchaseAsync()
    {
        if (SelectedPlan is null) return;

        IsWorking = true;
        Message = null;
        try
        {
            var outcome = await _store.PurchaseAsync(SelectedPlan.ProductId);
            switch (outcome.Kind)
            {
                case PurchaseResultKind.Purchased:
                    await _entitlements.RefreshAsync();
                    Close();
                    break;
                case PurchaseResultKind.Pending:
                    Message = AppResources.PaywallPendingMessage;
                    break;
                case PurchaseResultKind.Failed:
                    Message = string.Format(Culture, AppResources.PaywallPurchaseFailedFormat, outcome.FailureReason);
                    break;
                case PurchaseResultKind.Cancelled:
                    break;
            }
        }
        catch (StoreException ex)
        {
            Message = string.Format(Culture, AppResources.PaywallPurchaseFailedFormat, ex.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanRestore() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        IsWorking = true;
        Message = null;
        try
        {
            await _store.SyncAsync();
            await _entitlements.RefreshAsync();
            if (_entitlements.Current.CanWrite)
                Close();
            else
                Message = AppResources.PaywallRestoreNothingFound;
        }
        catch (StoreException)
        {
            Message = AppResources.PaywallStoreUnavailable;
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private void Later() => Close();

    [RelayCommand]
    private Task OpenTermsAsync() => _links.OpenAsync(StoreLinks.Terms(Culture));

    [RelayCommand]
    private Task OpenPrivacyAsync() => _links.OpenAsync(StoreLinks.Privacy(Culture));

    /// <summary>Ask to Buy approved, a renewal from another device: the sheet goes as soon as
    /// writing is allowed, whatever opened it.</summary>
    private void OnEntitlementsChanged(AccessState state)
    {
        if (state.CanWrite) Close();
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _entitlements.Changed -= OnEntitlementsChanged;
        Closed?.Invoke();
    }

    private void BuildPlans(IReadOnlyList<StoreProduct> products)
    {
        Plans.Clear();
        var monthly = products.FirstOrDefault(p => p.Id == StoreCatalog.BaseMonthly);
        var yearly = products.FirstOrDefault(p => p.Id == StoreCatalog.BaseYearly);

        // Yearly first and preselected: the plan most people should take is the one already chosen.
        if (yearly is not null)
            Plans.Add(Plan(yearly, AppResources.PaywallYearlyTitle, AppResources.PaywallPricePerYearFormat, DiscountBadge(monthly, yearly)));
        if (monthly is not null)
            Plans.Add(Plan(monthly, AppResources.PaywallMonthlyTitle, AppResources.PaywallPricePerMonthFormat, null));

        OnPropertyChanged(nameof(HasPlans));
        SelectPlan(Plans.FirstOrDefault());
    }

    private static PaywallPlan Plan(StoreProduct product, string title, string perPeriodFormat, string? badge)
    {
        var priceText = string.Format(Culture, perPeriodFormat, product.DisplayPrice);
        string? trial = null;
        if (product.IsEligibleForIntroOffer && product.IntroOffer is { Kind: StoreIntroOfferKind.FreeTrial } offer)
            trial = string.Format(Culture, AppResources.PaywallTrialFormat, Duration(offer.Period, offer.PeriodCount), priceText);
        return new PaywallPlan(product.Id, title, priceText, badge, trial);
    }

    /// <summary>"Save 53%": 12 months against a year, in the storefront's own prices. Absent when
    /// either price is missing or the year is not actually cheaper — no invented discounts (2.3.1).</summary>
    public static string? DiscountBadge(StoreProduct? monthly, StoreProduct? yearly)
    {
        if (monthly is null || yearly is null || monthly.Price <= 0) return null;
        var percent = (int)Math.Round((1m - yearly.Price / (monthly.Price * 12m)) * 100m, MidpointRounding.AwayFromZero);
        return percent > 0 ? string.Format(Culture, AppResources.PaywallDiscountFormat, percent) : null;
    }

    public static string Duration(SubscriptionPeriod period, int periodCount)
    {
        var count = period.Count * Math.Max(1, periodCount);
        return period.Unit switch
        {
            SubscriptionPeriodUnit.Day => Days(count),
            SubscriptionPeriodUnit.Week => Days(count * 7),
            SubscriptionPeriodUnit.Month => string.Format(Culture, AppResources.PaywallTrialMonthFormat, count),
            _ => string.Format(Culture, AppResources.PaywallTrialYearFormat, count)
        };
    }

    /// <summary>Ukrainian needs three forms (1 день, 3 дні, 14 днів); English uses the same rule
    /// with "days" for both plural slots.</summary>
    private static string Days(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var format = mod10 == 1 && mod100 != 11 ? AppResources.PaywallTrialDayOne
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? AppResources.PaywallTrialDayFew
            : AppResources.PaywallTrialDayMany;
        return string.Format(Culture, format, n);
    }
}
