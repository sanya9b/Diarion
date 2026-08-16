using System;
using Microsoft.Maui.Controls;

namespace Diarion.Behaviors;

/// <summary>
/// Lets a ViewModel say "the caret belongs here now" without knowing anything about views.
/// </summary>
/// <remarks>
/// <para>
/// The note editor rebuilds blocks as the user types — a line that becomes a tick box is a different
/// field from the one the marker was typed into, and it appears already focused only if something puts
/// the keyboard back. That is this.
/// </para>
/// <para>
/// <see cref="IsRequested"/> is two-way and cleared here once honoured, so the same block can ask again
/// later; a request that never cleared would also never fire a second time.
/// </para>
/// </remarks>
public class FocusRequestBehavior : Behavior<InputView>
{
    public static readonly BindableProperty IsRequestedProperty =
        BindableProperty.Create(nameof(IsRequested), typeof(bool), typeof(FocusRequestBehavior), false,
            BindingMode.TwoWay, propertyChanged: OnIsRequestedChanged);

    public static readonly BindableProperty CaretProperty =
        BindableProperty.Create(nameof(Caret), typeof(int), typeof(FocusRequestBehavior), 0);

    private InputView? _view;

    public bool IsRequested
    {
        get => (bool)GetValue(IsRequestedProperty);
        set => SetValue(IsRequestedProperty, value);
    }

    /// <summary>Where in the text the caret should land.</summary>
    public int Caret
    {
        get => (int)GetValue(CaretProperty);
        set => SetValue(CaretProperty, value);
    }

    protected override void OnAttachedTo(InputView bindable)
    {
        base.OnAttachedTo(bindable);

        _view = bindable;
        BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += OnViewBindingContextChanged;

        // A block that was created already asking for focus attaches with the request standing.
        Apply();
    }

    protected override void OnDetachingFrom(InputView bindable)
    {
        bindable.BindingContextChanged -= OnViewBindingContextChanged;
        bindable.HandlerChanged -= OnHandlerChanged;
        _view = null;
        BindingContext = null;

        base.OnDetachingFrom(bindable);
    }

    private void OnViewBindingContextChanged(object? sender, EventArgs e)
    {
        BindingContext = _view?.BindingContext;
    }

    private static void OnIsRequestedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if ((bool)newValue) ((FocusRequestBehavior)bindable).Apply();
    }

    private void Apply()
    {
        if (!IsRequested || _view is null) return;

        // Focus does nothing before the platform control exists, which is exactly the case for a block
        // that was just inserted into the list. Wait for it rather than dropping the request.
        if (_view.Handler is null)
        {
            _view.HandlerChanged -= OnHandlerChanged;
            _view.HandlerChanged += OnHandlerChanged;
            return;
        }

        var view = _view;
        var caret = Caret;

        view.Dispatcher.Dispatch(() =>
        {
            if (!ReferenceEquals(view, _view)) return;

            view.Focus();

            // After Focus: on some platforms taking focus selects the text, which would put the caret
            // at the end and lose the position the editor worked out.
            var length = (view.Text ?? string.Empty).Length;
            view.CursorPosition = Math.Clamp(caret, 0, length);
            view.SelectionLength = 0;

            IsRequested = false;
        });
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not InputView view) return;
        if (view.Handler is null) return;

        view.HandlerChanged -= OnHandlerChanged;
        Apply();
    }
}
