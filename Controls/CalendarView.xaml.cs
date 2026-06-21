using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Diarion.Controls;

public partial class CalendarView : ContentView
{
    public static readonly BindableProperty IsExpandedProperty = BindableProperty.Create(
        nameof(IsExpanded), typeof(bool), typeof(CalendarView), false, propertyChanged: OnIsExpandedChanged);

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public CalendarView()
    {
        InitializeComponent();
    }

    private static async void OnIsExpandedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CalendarView view && newValue is bool isExpanded)
        {
            await view.AnimateExpansion(isExpanded);
        }
    }

    private async Task AnimateExpansion(bool isExpanded)
    {
        if (isExpanded)
        {
            CalendarHeader.IsVisible = true;
            CalendarGrid.IsVisible = true;

            // Wait until layout is somewhat stable or just use a default estimate
            // We can calculate rows if we have access to CalendarDays.
            // But we don't have direct access here easily unless we cast BindingContext.
            // As a fallback, we can use a fixed estimated height or measure it.
            
            // For simplicity, let's just fade them in and use Auto size.
            CalendarSection.HeightRequest = -1; 
            
            await Task.WhenAll(
                CalendarHeader.FadeToAsync(1, 200, Easing.CubicOut),
                CalendarGrid.FadeToAsync(1, 200, Easing.CubicOut)
            );
        }
        else
        {
            await Task.WhenAll(
                CalendarHeader.FadeToAsync(0, 150, Easing.CubicIn),
                CalendarGrid.FadeToAsync(0, 150, Easing.CubicIn)
            );
            CalendarHeader.IsVisible = false;
            CalendarGrid.IsVisible = false;
        }
    }
}