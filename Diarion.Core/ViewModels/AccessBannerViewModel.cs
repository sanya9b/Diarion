using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels;

/// <summary>
/// The two things the home screen says about access, at most one at a time: the read-only banner
/// with its single "Renew" button (spec 15, R5.1), or — once, after the update — the card telling
/// a 1.0 user the app stays free for them (R2.4).
/// </summary>
public partial class AccessBannerViewModel : ObservableObject, IDisposable
{
    internal const string LegacyCardDismissedKey = "access.legacyCardDismissed";

    private readonly IEntitlementService _entitlements;
    private readonly IEntitlementStore _store;
    private readonly IDispatcherService _dispatcher;

    public AccessBannerViewModel(IEntitlementService entitlements, IEntitlementStore store, IDispatcherService dispatcher)
    {
        _entitlements = entitlements;
        _store = store;
        _dispatcher = dispatcher;
        Apply(entitlements.Current);
        _entitlements.Changed += OnChanged;
    }

    [ObservableProperty]
    private bool _isReadOnly;

    /// <summary>
    /// "Your subscription ended" only to someone who had one. A new user who chose "Later" after
    /// onboarding is read-only too, and for them the banner is an invitation, not news of a loss
    /// (spec 15, R4.3).
    /// </summary>
    [ObservableProperty]
    private string _bannerText = AppResources.ReadOnlyBannerText;

    [ObservableProperty]
    private string _bannerButtonText = AppResources.ReadOnlyBannerButton;

    [ObservableProperty]
    private bool _showLegacyCard;

    [RelayCommand]
    private void Renew() =>
        WeakReferenceMessenger.Default.Send(new PaywallRequestedMessage(PaywallReason.UserRequested));

    [RelayCommand]
    private void DismissLegacyCard()
    {
        _store.Set(LegacyCardDismissedKey, "1");
        ShowLegacyCard = false;
    }

    public void Dispose() => _entitlements.Changed -= OnChanged;

    private void OnChanged(AccessState state) => _dispatcher.InvokeOnMainThread(() => Apply(state));

    private void Apply(AccessState state)
    {
        IsReadOnly = state.IsReadOnly;
        var hadSubscription = _store.Get(EntitlementService.HadSubscriptionKey) == "1";
        BannerText = hadSubscription ? AppResources.ReadOnlyBannerText : AppResources.ReadOnlyBannerNewText;
        BannerButtonText = hadSubscription ? AppResources.ReadOnlyBannerButton : AppResources.ReadOnlyBannerNewButton;
        ShowLegacyCard = state.Source == AccessSource.Legacy && _store.Get(LegacyCardDismissedKey) != "1";
    }
}
