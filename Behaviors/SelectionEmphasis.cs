using Diarion.Resources.Styles;
using Microsoft.Maui.Controls;

namespace Diarion.Behaviors;

/// <summary>
/// Attached switch that grows an element slightly while it is the chosen one:
/// <c>behaviors:SelectionEmphasis.IsActive="{Binding IsSelected}"</c>. Animated on the <see cref="Motion"/>
/// tokens; under reduce motion the scale collapses to 1 and the fill alone says "chosen".
/// Put it on an inner element, never on the tap target itself: <see cref="PressFeedback"/> animates the
/// target's own Scale back to 1 after every press and would undo the emphasis.
/// </summary>
public static class SelectionEmphasis
{
    /// <summary>Subtle on purpose: the user sees this dozens of times a day.</summary>
    public const double ActiveScale = 1.08;

    public static readonly BindableProperty IsActiveProperty = BindableProperty.CreateAttached(
        "IsActive", typeof(bool), typeof(SelectionEmphasis), false, propertyChanged: OnIsActiveChanged);

    public static bool GetIsActive(BindableObject view) => (bool)view.GetValue(IsActiveProperty);

    public static void SetIsActive(BindableObject view, bool value) => view.SetValue(IsActiveProperty, value);

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement view)
            return;

        var target = (bool)newValue ? Motion.Scale(ActiveScale) : 1.0;
        view.CancelAnimations();

        // Before the element is on screen there is nothing to animate from: the first bind should
        // show what was already true, not perform it.
        if (!view.IsLoaded)
        {
            view.Scale = target;
            return;
        }

        _ = view.ScaleToAsync(target, Motion.FastMs, Motion.EaseOut);
    }
}
