using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Diarion.Resources.Styles;
using Microsoft.Maui.Controls;

namespace Diarion.Controls;

/// <summary>
/// Code-behind for the shared empty state. Properties are pushed to the named children rather than
/// bound, so the control works inside a <c>CollectionView.EmptyView</c> too, where bindings to the
/// page's context do not resolve. Appearance is animated once each time the view becomes visible,
/// on the motion tokens; under reduce motion only the fade remains.
/// </summary>
public partial class EmptyStateView : ContentView
{
    public static readonly BindableProperty SceneProperty =
        BindableProperty.Create(nameof(Scene), typeof(EmptyScene), typeof(EmptyStateView), EmptyScene.Notes,
            propertyChanged: (b, _, v) => ((EmptyStateView)b).Picture.Scene = (EmptyScene)v);

    public EmptyScene Scene
    {
        get => (EmptyScene)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyStateView), string.Empty,
            propertyChanged: (b, _, v) => ((EmptyStateView)b).ApplyText());

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly BindableProperty HintProperty =
        BindableProperty.Create(nameof(Hint), typeof(string), typeof(EmptyStateView), string.Empty,
            propertyChanged: (b, _, v) => ((EmptyStateView)b).ApplyText());

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public static readonly BindableProperty ActionTextProperty =
        BindableProperty.Create(nameof(ActionText), typeof(string), typeof(EmptyStateView), string.Empty,
            propertyChanged: (b, _, v) => ((EmptyStateView)b).ApplyAction());

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public static readonly BindableProperty ActionCommandProperty =
        BindableProperty.Create(nameof(ActionCommand), typeof(ICommand), typeof(EmptyStateView), null,
            propertyChanged: (b, _, v) => ((EmptyStateView)b).ApplyAction());

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>
    /// A smaller rendition for empty states that live inside a card rather than filling a screen
    /// (the cycle history): 64 dp scene, tighter padding, smaller type.
    /// </summary>
    public static readonly BindableProperty CompactProperty =
        BindableProperty.Create(nameof(Compact), typeof(bool), typeof(EmptyStateView), false,
            propertyChanged: (b, _, v) => ((EmptyStateView)b).ApplyDensity());

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private bool _shown;

    public EmptyStateView()
    {
        InitializeComponent();
        ActionButton.Clicked += (_, _) =>
        {
            if (ActionCommand?.CanExecute(null) == true)
                ActionCommand.Execute(null);
        };
        Loaded += (_, _) =>
        {
            if (IsVisible)
                _ = AppearAsync();
        };
    }

    private void ApplyText()
    {
        TitleLabel.Text = Title;
        TitleLabel.IsVisible = !string.IsNullOrEmpty(Title);
        HintLabel.Text = Hint;
        HintLabel.IsVisible = !string.IsNullOrEmpty(Hint);
    }

    private void ApplyAction()
    {
        ActionButton.Text = ActionText;
        ActionButton.IsVisible = ActionCommand is not null && !string.IsNullOrEmpty(ActionText);
    }

    private void ApplyDensity()
    {
        var compact = Compact;
        Picture.WidthRequest = Picture.HeightRequest = compact ? 64 : 96;
        Stack.Padding = compact ? new Thickness(8, 12) : new Thickness(24, 32);
        Stack.Spacing = compact ? 8 : 12;
        TitleLabel.FontSize = compact ? 14 : 16;
        HintLabel.FontSize = compact ? 12 : 13;
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName != IsVisibleProperty.PropertyName)
            return;

        if (IsVisible)
        {
            if (IsLoaded)
                _ = AppearAsync();
        }
        else
        {
            // Hidden again (the list got its first item); the next appearance is a fresh one.
            _shown = false;
        }
    }

    // Spec 16, R3.4: the template from ui-motion §6, once per appearance. Nothing here is on a daily
    // path, so the standard duration is within the gate.
    private async Task AppearAsync()
    {
        if (_shown)
            return;
        _shown = true;

        Stack.Opacity = 0;
        Stack.Scale = Motion.Scale(0.96);
        await Task.WhenAll(
            Stack.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut),
            Stack.ScaleToAsync(1, Motion.StandardMs, Motion.EaseOut));
    }
}
