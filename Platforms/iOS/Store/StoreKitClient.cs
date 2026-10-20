using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Store;

/// <summary>
/// The iOS <see cref="IStoreClient"/>: P/Invokes into the static StoreKit 2 facade linked from
/// <c>native/ios-storekit</c> (spec 15, R3.4). Each call hands the facade a
/// <see cref="TaskCompletionSource{TResult}"/> pinned in a <see cref="GCHandle"/>; the facade
/// completes it once from whatever thread StoreKit answers on. Strings go over as UTF-8 and are
/// freed as soon as the call returns — the Swift side copies them before it awaits anything.
/// </summary>
public sealed class StoreKitClient : IStoreClient, IDisposable
{
    private readonly GCHandle _self;

    public StoreKitClient()
    {
        _self = GCHandle.Alloc(this, GCHandleType.Weak);
        Native.StartUpdates(GCHandle.ToIntPtr(_self));
    }

    public bool IsAvailable => true;

    public event Action? TransactionsChanged;

    public async Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken = default)
    {
        var json = await WithUtf8(JsonSerializer.Serialize(productIds), Native.LoadProducts, cancellationToken);
        return StoreKitJson.Products(json);
    }

    public async Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken cancellationToken = default)
    {
        var json = await WithUtf8(productId, Native.Purchase, cancellationToken);
        return StoreKitJson.Purchase(json);
    }

    public async Task<IReadOnlyList<StoreEntitlement>> GetEntitlementsAsync(CancellationToken cancellationToken = default)
    {
        var json = await Call(Native.CurrentEntitlements, cancellationToken);
        return StoreKitJson.Entitlements(json);
    }

    public async Task<IReadOnlyList<SubscriptionStatus>> GetSubscriptionStatusesAsync(CancellationToken cancellationToken = default)
    {
        var json = await WithUtf8(JsonSerializer.Serialize(StoreCatalog.AllProductIds), Native.SubscriptionStatuses, cancellationToken);
        return StoreKitJson.Statuses(json);
    }

    public Task SyncAsync(CancellationToken cancellationToken = default) =>
        Call(Native.Sync, cancellationToken);

    public async Task<AppTransactionInfo?> GetAppTransactionAsync(CancellationToken cancellationToken = default)
    {
        var json = await Call(Native.AppTransaction, cancellationToken);
        return StoreKitJson.AppTransaction(json);
    }

    public Task ShowManageSubscriptionsAsync(CancellationToken cancellationToken = default) =>
        Call(Native.ManageSubscriptions, cancellationToken);

    public void Dispose()
    {
        if (_self.IsAllocated) _self.Free();
    }

    private static Task<string> Call(Action<IntPtr> invoke, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = GCHandle.ToIntPtr(GCHandle.Alloc(completion));
        invoke(context);
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static Task<string> WithUtf8(string text, Action<IntPtr, IntPtr> invoke, CancellationToken cancellationToken)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return Call(context => invoke(utf8, context), cancellationToken);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnComplete(IntPtr context, byte ok, IntPtr json)
    {
        var handle = GCHandle.FromIntPtr(context);
        var completion = (TaskCompletionSource<string>?)handle.Target;
        handle.Free();
        if (completion is null) return;

        var text = json == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(json) ?? string.Empty;
        if (ok == 1)
            completion.TrySetResult(text);
        else
            completion.TrySetException(new StoreException(text.Length == 0 ? "StoreKit returned an error" : text));
    }

    [UnmanagedCallersOnly]
    private static void OnUpdate(IntPtr context, byte ok, IntPtr json)
    {
        if (GCHandle.FromIntPtr(context).Target is StoreKitClient client)
            client.TransactionsChanged?.Invoke();
    }

    /// <summary>The only unsafe code in the app: the eight entry points and the function pointers
    /// they take. Kept apart so the async methods above stay in a safe context.</summary>
    private static unsafe class Native
    {
        public static void LoadProducts(IntPtr productIdsJson, IntPtr context) => dsk_load_products(productIdsJson, context, &OnComplete);
        public static void Purchase(IntPtr productId, IntPtr context) => dsk_purchase(productId, context, &OnComplete);
        public static void CurrentEntitlements(IntPtr context) => dsk_current_entitlements(context, &OnComplete);
        public static void SubscriptionStatuses(IntPtr productIdsJson, IntPtr context) => dsk_subscription_statuses(productIdsJson, context, &OnComplete);
        public static void Sync(IntPtr context) => dsk_sync(context, &OnComplete);
        public static void AppTransaction(IntPtr context) => dsk_app_transaction(context, &OnComplete);
        public static void ManageSubscriptions(IntPtr context) => dsk_manage_subscriptions(context, &OnComplete);
        public static void StartUpdates(IntPtr context) => dsk_start_updates(context, &OnUpdate);

        [DllImport("__Internal")]
        private static extern void dsk_load_products(IntPtr productIdsJson, IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);

        [DllImport("__Internal")]
        private static extern void dsk_purchase(IntPtr productId, IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);

        [DllImport("__Internal")]
        private static extern void dsk_current_entitlements(IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);

        [DllImport("__Internal")]
        private static extern void dsk_subscription_statuses(IntPtr productIdsJson, IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);

        [DllImport("__Internal")]
        private static extern void dsk_sync(IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);

        [DllImport("__Internal")]
        private static extern void dsk_app_transaction(IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);

        [DllImport("__Internal")]
        private static extern void dsk_manage_subscriptions(IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);

        [DllImport("__Internal")]
        private static extern void dsk_start_updates(IntPtr context, delegate* unmanaged<IntPtr, byte, IntPtr, void> callback);
    }
}
