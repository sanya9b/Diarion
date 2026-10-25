using System;
using System.Windows.Input;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// The app's only tick box; see the XAML for why the native CheckBox is not used.
/// Without a <see cref="Command"/> a tap flips <see cref="IsChecked"/> (two-way bound by default).
/// With one, the tap runs the command and leaves the state to it.
/// </summary>
public partial class RoundedCheckBox : ContentView
{
    public static readonly BindableProperty IsCheckedProperty =
        BindableProperty.Create(nameof(IsChecked), typeof(bool), typeof(RoundedCheckBox), false, BindingMode.TwoWay);

    /// <summary>Fill and stroke of the ticked box. Null falls back to the theme's Coral.</summary>
    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(RoundedCheckBox), null, propertyChanged: OnColorChanged);

    /// <summary>6 on a 20px box everywhere; nothing asks for a circle any more.</summary>
    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(CornerRadius), typeof(RoundedCheckBox), new CornerRadius(6));

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(RoundedCheckBox));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(RoundedCheckBox));

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public Color? Color
    {
        get => (Color?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private Color _activeColor = Colors.Gray;

    /// <summary>What the ticked box is painted with: <see cref="Color"/>, or Coral when none was given.</summary>
    public Color ActiveColor
    {
        get => _activeColor;
        private set
        {
            if (_activeColor == value) return;
            _activeColor = value;
            OnPropertyChanged(nameof(ActiveColor));
        }
    }

    public RoundedCheckBox()
    {
        InitializeComponent();
        UpdateActiveColor();
    }

    private static void OnColorChanged(BindableObject bindable, object oldValue, object newValue)
        => ((RoundedCheckBox)bindable).UpdateActiveColor();

    private void UpdateActiveColor()
    {
        var color = Color;
        if (color == null
            && Application.Current?.Resources != null
            && Application.Current.Resources.TryGetValue("Theme_Coral", out var coral)
            && coral is Color coralColor)
        {
            color = coralColor;
        }

        ActiveColor = color ?? Colors.Gray;
    }

    private void OnButtonClicked(object? sender, EventArgs e)
    {
        var command = Command;
        if (command != null)
        {
            if (command.CanExecute(CommandParameter))
            {
                command.Execute(CommandParameter);
            }
            return;
        }

        IsChecked = !IsChecked;
    }
}
