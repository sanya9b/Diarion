using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public partial class RoundedCheckBox : ContentView
{
    public static readonly BindableProperty IsCheckedProperty =
        BindableProperty.Create(nameof(IsChecked), typeof(bool), typeof(RoundedCheckBox), false, BindingMode.TwoWay, propertyChanged: OnIsCheckedChanged);

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(RoundedCheckBox), null, propertyChanged: OnColorChanged);

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private Color _computedBackgroundColor = Colors.Transparent;
    public Color ComputedBackgroundColor
    {
        get => _computedBackgroundColor;
        private set
        {
            if (_computedBackgroundColor != value)
            {
                _computedBackgroundColor = value;
                OnPropertyChanged(nameof(ComputedBackgroundColor));
            }
        }
    }

    public RoundedCheckBox()
    {
        InitializeComponent();
        UpdateBackgroundColor();
    }

    private void OnButtonClicked(object? sender, EventArgs e)
    {
        IsChecked = !IsChecked;
    }

    private static void OnIsCheckedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var cb = (RoundedCheckBox)bindable;
        cb.UpdateBackgroundColor();
    }

    private static void OnColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var cb = (RoundedCheckBox)bindable;
        cb.UpdateBackgroundColor();
    }

    private void UpdateBackgroundColor()
    {
        Color activeColor = Color;
        if (activeColor == null)
        {
            if (Application.Current?.Resources != null && Application.Current.Resources.TryGetValue("Theme_Coral", out var coralResource) && coralResource is Color coralColor)
            {
                activeColor = coralColor;
            }
            else
            {
                activeColor = Colors.Gray;
            }
        }

        ComputedBackgroundColor = IsChecked ? activeColor : Colors.Transparent;
    }
}