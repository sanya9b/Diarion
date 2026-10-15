using Microsoft.Maui.Controls;

namespace Diarion.Behaviors;

/// <summary>
/// Attached properties that bind a colour to a <em>theme resource key</em> coming from a view model:
/// <c>behaviors:ThemeColor.BackgroundKey="{Binding FillColorKey}"</c>. The view model names the role
/// ("Theme_MoodPleasantCalm"); the current theme dictionary says what colour that is, and keeps
/// saying so when the theme changes, because the key is applied as a dynamic resource. A null or
/// empty key clears the property, which for a fill layer means see-through.
/// </summary>
/// <remarks>
/// This exists because a view model in Core cannot know the theme, and because a DataTrigger on a
/// Border that also carries press feedback does not reliably revert its colour; a bound key has nothing
/// to revert.
/// </remarks>
public static class ThemeColor
{
    public static readonly BindableProperty BackgroundKeyProperty = BindableProperty.CreateAttached(
        "BackgroundKey", typeof(string), typeof(ThemeColor), null,
        propertyChanged: (b, _, n) => Apply(b, VisualElement.BackgroundColorProperty, n as string));

    public static readonly BindableProperty TextKeyProperty = BindableProperty.CreateAttached(
        "TextKey", typeof(string), typeof(ThemeColor), null,
        propertyChanged: (b, _, n) => Apply(b, Label.TextColorProperty, n as string));

    public static string? GetBackgroundKey(BindableObject view) => (string?)view.GetValue(BackgroundKeyProperty);

    public static void SetBackgroundKey(BindableObject view, string? value) => view.SetValue(BackgroundKeyProperty, value);

    public static string? GetTextKey(BindableObject view) => (string?)view.GetValue(TextKeyProperty);

    public static void SetTextKey(BindableObject view, string? value) => view.SetValue(TextKeyProperty, value);

    private static void Apply(BindableObject target, BindableProperty property, string? key)
    {
        if (target is not Element element) return;

        // Specificity is the whole game here. A value that arrived through a dynamic resource outlives
        // RemoveDynamicResource and is not touched by ClearValue, which only clears a manual value — so
        // "no key" has to be an explicit manual Transparent. And a manual value outranks any dynamic
        // resource set later, so it has to be cleared again before the next key can show.
        if (string.IsNullOrEmpty(key))
        {
            element.RemoveDynamicResource(property);
            element.SetValue(property, Colors.Transparent);
            return;
        }

        element.ClearValue(property);
        element.SetDynamicResource(property, key);
    }
}
