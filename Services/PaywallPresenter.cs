using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Services.Store;
using Diarion.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Diarion.Services;

/// <summary>
/// Shows the paywall when anything asks for it (spec 15, R4.2, R5.2): a blocked write, "Renew",
/// "Subscribe" in Settings, the end of onboarding. One sheet at a time, modal over whatever is on
/// screen, and only where a store exists to pay through.
/// </summary>
public sealed class PaywallPresenter : IRecipient<PaywallRequestedMessage>
{
    private readonly IServiceProvider _services;
    private readonly IEntitlementService _entitlements;
    private readonly IStoreClient _store;
    private bool _showing;

    public PaywallPresenter(IServiceProvider services, IEntitlementService entitlements, IStoreClient store)
    {
        _services = services;
        _entitlements = entitlements;
        _store = store;
    }

    public void Start() => WeakReferenceMessenger.Default.Register(this);

    public void Receive(PaywallRequestedMessage message) =>
        MainThread.BeginInvokeOnMainThread(() => _ = ShowAsync(message.Reason));

    private async Task ShowAsync(PaywallReason reason)
    {
        if (_showing || !_store.IsAvailable) return;

        // A blocked write or the post-onboarding prompt mean nothing to someone who may write; a
        // tap on "Support with a subscription" does.
        if (reason != PaywallReason.UserRequested && _entitlements.Current.CanWrite) return;

        var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (navigation is null) return;

        var page = _services.GetRequiredService<PaywallPage>();
        _showing = true;
        page.ViewModel.Closed = () => MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (navigation.ModalStack.Contains(page))
                    await navigation.PopModalAsync();
            }
            finally
            {
                _showing = false;
            }
        });

        try
        {
            await navigation.PushModalAsync(page);
        }
        catch
        {
            _showing = false;
            throw;
        }
    }
}
