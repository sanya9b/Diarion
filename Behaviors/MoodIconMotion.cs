using Diarion.Resources.Styles;
using Microsoft.Maui.Controls;

namespace Diarion.Behaviors;

/// <summary>
/// How a mood face reacts to being chosen: <c>behaviors:MoodIconMotion.IsActive="{Binding IsSelected}"</c>.
/// Faces not chosen sit a little dimmed, so the five read as a calm row; the chosen one comes up to full
/// strength and lifts a few points and settles back — a nod to the tap. Purpose: feedback. No bounce,
/// no loop: the row is touched several times a day. Under reduce motion only the dimming changes.
/// </summary>
public static class MoodIconMotion
{
    /// <summary>A face that is not chosen.</summary>
    public const double RestOpacity = 0.62;

    /// <summary>How far the chosen face lifts before it settles, in points.</summary>
    private const double Lift = 4;

    public static readonly BindableProperty IsActiveProperty = BindableProperty.CreateAttached(
        "IsActive", typeof(bool), typeof(MoodIconMotion), false, propertyChanged: OnIsActiveChanged);

    public static bool GetIsActive(BindableObject view) => (bool)view.GetValue(IsActiveProperty);

    public static void SetIsActive(BindableObject view, bool value) => view.SetValue(IsActiveProperty, value);

    private static async void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement view) return;

        var active = (bool)newValue;
        view.CancelAnimations();

        // Before it is on screen there is nothing to animate from: show what is already true.
        if (!view.IsLoaded)
        {
            view.Opacity = active ? 1 : RestOpacity;
            view.TranslationY = 0;
            return;
        }

        if (!active)
        {
            view.TranslationY = 0;
            await view.FadeToAsync(RestOpacity, Motion.FastMs, Motion.EaseOut);
            return;
        }

        var fade = view.FadeToAsync(1, Motion.FastMs, Motion.EaseOut);
        if (Motion.ReduceMotion)
        {
            await fade;
            return;
        }

        await view.TranslateToAsync(0, -Lift, (uint)(Motion.StandardMs * 0.4), Motion.EaseOut);
        await view.TranslateToAsync(0, 0, (uint)(Motion.StandardMs * 0.6), Motion.EaseOut);
        await fade;
    }
}
