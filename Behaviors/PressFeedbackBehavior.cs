using System.Linq;
using CommunityToolkit.Maui.Behaviors;
using Diarion.Resources.Styles;
using Microsoft.Maui.Controls;

namespace Diarion.Behaviors;

/// <summary>
/// Pressed-state feedback for tap targets that are not <see cref="Button"/>s: a near-imperceptible
/// scale and dim, on the <see cref="Motion"/> tokens. Under reduce motion only the dim remains.
/// Do not attach to elements carrying drag/drop gestures, or to containers whose children must stay
/// interactive — TouchBehavior makes children input-transparent.
/// </summary>
public sealed class PressFeedbackBehavior : TouchBehavior
{
    public PressFeedbackBehavior()
    {
        PressedScale = Motion.Scale(0.97);
        PressedOpacity = 0.9;
        PressedAnimationDuration = Motion.PressMs;
        PressedAnimationEasing = Motion.EaseOut;
        DefaultAnimationDuration = Motion.PressMs;
        DefaultAnimationEasing = Motion.EaseOut;
    }
}

/// <summary>
/// Attached switch so press feedback can be turned on from a Style setter or inline:
/// <c>behaviors:PressFeedback.IsEnabled="True"</c>.
/// </summary>
public static class PressFeedback
{
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), false, propertyChanged: OnIsEnabledChanged);

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement view)
            return;

        var existing = view.Behaviors.OfType<PressFeedbackBehavior>().FirstOrDefault();
        if ((bool)newValue)
        {
            if (existing is null)
                view.Behaviors.Add(new PressFeedbackBehavior());
        }
        else if (existing is not null)
        {
            view.Behaviors.Remove(existing);
        }
    }
}
