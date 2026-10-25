using System;
using Diarion.Services.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

namespace Diarion.Behaviors;

/// <summary>
/// <c>behaviors:ReadOnlyAccess.DisableWhenReadOnly="True"</c> on a container turns everything in it
/// off while the diary is read-only, and back on when access returns (spec 15, R5.1). It is for the
/// places where text is typed and saved on the fly — the home blocks, a note, an entry — so that
/// nobody writes a paragraph only to be told at the autosave that it will not be kept. Reading and
/// scrolling are untouched: the container sits inside its ScrollView, not around it.
/// </summary>
public static class ReadOnlyAccess
{
    public static readonly BindableProperty DisableWhenReadOnlyProperty = BindableProperty.CreateAttached(
        "DisableWhenReadOnly", typeof(bool), typeof(ReadOnlyAccess), false, propertyChanged: OnDisableWhenReadOnlyChanged);

    private static readonly BindableProperty SubscriptionProperty = BindableProperty.CreateAttached(
        "Subscription", typeof(Subscription), typeof(ReadOnlyAccess), null);

    public static bool GetDisableWhenReadOnly(BindableObject view) => (bool)view.GetValue(DisableWhenReadOnlyProperty);

    public static void SetDisableWhenReadOnly(BindableObject view, bool value) => view.SetValue(DisableWhenReadOnlyProperty, value);

    private static void OnDisableWhenReadOnlyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element) return;

        (element.GetValue(SubscriptionProperty) as Subscription)?.Detach();
        element.SetValue(SubscriptionProperty, null);
        if (newValue is not true) return;

        var entitlements = IPlatformApplication.Current?.Services.GetService<IEntitlementService>();
        if (entitlements is null) return;

        var subscription = new Subscription(element, entitlements);
        element.SetValue(SubscriptionProperty, subscription);
        subscription.Attach();
    }

    /// <summary>Follows the access state while the element is on screen; lets go when it leaves, so
    /// a page that is gone is not kept alive by a singleton's event.</summary>
    private sealed class Subscription
    {
        private readonly VisualElement _element;
        private readonly IEntitlementService _entitlements;

        public Subscription(VisualElement element, IEntitlementService entitlements)
        {
            _element = element;
            _entitlements = entitlements;
        }

        public void Attach()
        {
            _element.Loaded += OnLoaded;
            _element.Unloaded += OnUnloaded;
            Apply(_entitlements.Current);
            if (_element.IsLoaded) _entitlements.Changed += OnAccessChanged;
        }

        public void Detach()
        {
            _element.Loaded -= OnLoaded;
            _element.Unloaded -= OnUnloaded;
            _entitlements.Changed -= OnAccessChanged;
        }

        private void OnLoaded(object? sender, EventArgs e)
        {
            Apply(_entitlements.Current);
            _entitlements.Changed -= OnAccessChanged;
            _entitlements.Changed += OnAccessChanged;
        }

        private void OnUnloaded(object? sender, EventArgs e) => _entitlements.Changed -= OnAccessChanged;

        private void OnAccessChanged(AccessState state) => _element.Dispatcher.Dispatch(() => Apply(state));

        private void Apply(AccessState state) => _element.IsEnabled = state.CanWrite;
    }
}
