using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels;

/// <summary>
/// The "Subscription" card in Settings → Data (spec 15, R4.4): what the state is, until when, and
/// the one or two actions that make sense from it. Hidden where there is no store at all.
/// </summary>
public partial class SubscriptionSectionViewModel : ObservableObject, IDisposable
{
    private readonly IEntitlementService _entitlements;
    private readonly IStoreClient _store;
    private readonly IDispatcherService _dispatcher;

    public SubscriptionSectionViewModel(IEntitlementService entitlements, IStoreClient store, IDispatcherService dispatcher)
    {
        _entitlements = entitlements;
        _store = store;
        _dispatcher = dispatcher;
        Apply(entitlements.Current);
        _entitlements.Changed += OnChanged;
    }

    /// <summary>No store on this platform: the card is not shown at all.</summary>
    public bool IsVisible => _store.IsAvailable;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>A live subscription: Apple's own sheet changes plan or cancels it.</summary>
    [ObservableProperty]
    private bool _canManage;

    /// <summary>Read-only: the way back is the paywall.</summary>
    [ObservableProperty]
    private bool _canSubscribe;

    /// <summary>A 1.0 user who wants to pay anyway. Never pushed, only offered here.</summary>
    [ObservableProperty]
    private bool _canSupport;

    /// <summary>
    /// Restore, for everyone the store could owe a subscription to (R4.4) — a new phone, a reinstall.
    /// Not for a 1.0 user, who has nothing to restore and would only be told so.
    /// </summary>
    [ObservableProperty]
    private bool _canRestore;

    /// <summary>What the last Restore found when it found nothing; the status line says the rest.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestoreMessage))]
    private string? _restoreMessage;

    public bool HasRestoreMessage => !string.IsNullOrEmpty(RestoreMessage);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isRestoring;

    private static CultureInfo Culture => AppResources.Culture ?? CultureInfo.CurrentUICulture;

    [RelayCommand]
    private async Task ManageAsync()
    {
        try
        {
            await _store.ShowManageSubscriptionsAsync();
        }
        catch (StoreException)
        {
            // Apple's sheet failed to open; there is nothing more useful to say than the store would.
        }
    }

    private bool CanRunRestore() => !IsRestoring;

    [RelayCommand(CanExecute = nameof(CanRunRestore))]
    private async Task RestoreAsync()
    {
        IsRestoring = true;
        RestoreMessage = null;
        try
        {
            await _store.SyncAsync();
            await _entitlements.RefreshAsync();
            if (!_entitlements.Current.CanWrite)
                RestoreMessage = AppResources.PaywallRestoreNothingFound;
        }
        catch (StoreException)
        {
            RestoreMessage = AppResources.PaywallStoreUnavailable;
        }
        finally
        {
            IsRestoring = false;
        }
    }

    [RelayCommand]
    private void Subscribe() =>
        WeakReferenceMessenger.Default.Send(new PaywallRequestedMessage(PaywallReason.UserRequested));

    public void Dispose() => _entitlements.Changed -= OnChanged;

    private void OnChanged(AccessState state) => _dispatcher.InvokeOnMainThread(() => Apply(state));

    private void Apply(AccessState state)
    {
        StatusText = Describe(state);
        CanManage = state.Source is AccessSource.Subscription or AccessSource.Trial or AccessSource.GracePeriod;
        CanSubscribe = state.IsReadOnly;
        CanSupport = state.Source == AccessSource.Legacy && _store.IsAvailable;
        CanRestore = state.Source != AccessSource.Legacy && _store.IsAvailable;
        if (state.CanWrite) RestoreMessage = null;
    }

    public static string Describe(AccessState state) => state.Source switch
    {
        AccessSource.Legacy => Format(AppResources.SubscriptionStatusLegacy, state.ExpiresAt),
        AccessSource.StoreUnavailable => AppResources.SubscriptionStatusUnavailable,
        AccessSource.Trial => Format(AppResources.SubscriptionStatusTrialFormat, state.ExpiresAt),
        AccessSource.GracePeriod => Format(AppResources.SubscriptionStatusGraceFormat, state.ExpiresAt),
        AccessSource.Subscription => Format(
            state.WillAutoRenew ? AppResources.SubscriptionStatusRenewsFormat : AppResources.SubscriptionStatusEndsFormat,
            state.ExpiresAt),
        _ => AppResources.SubscriptionStatusReadOnly
    };

    private static string Format(string format, DateTime? utc) =>
        string.Format(Culture, format, utc is null ? "—" : utc.Value.ToLocalTime().ToString("d MMMM yyyy", Culture));
}
