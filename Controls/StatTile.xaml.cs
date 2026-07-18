using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// A compact KPI tile: a small accent label, a bold value, and an optional muted caption,
/// laid out inside a themed card. Used to build the at-a-glance row above each statistics chart.
/// </summary>
public partial class StatTile : ContentView
{
    public static readonly BindableProperty LabelProperty =
        BindableProperty.Create(nameof(Label), typeof(string), typeof(StatTile), string.Empty);

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(StatTile), string.Empty);

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(StatTile), string.Empty,
            propertyChanged: OnCaptionChanged);

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(StatTile), Color.FromArgb("#C26D53"));

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public static readonly BindableProperty HasCaptionProperty =
        BindableProperty.Create(nameof(HasCaption), typeof(bool), typeof(StatTile), false);

    public bool HasCaption
    {
        get => (bool)GetValue(HasCaptionProperty);
        private set => SetValue(HasCaptionProperty, value);
    }

    public static readonly BindableProperty SparklineProperty =
        BindableProperty.Create(nameof(Sparkline), typeof(IEnumerable<double?>), typeof(StatTile), null,
            propertyChanged: OnSparklineChanged);

    public IEnumerable<double?>? Sparkline
    {
        get => (IEnumerable<double?>?)GetValue(SparklineProperty);
        set => SetValue(SparklineProperty, value);
    }

    public static readonly BindableProperty HasSparklineProperty =
        BindableProperty.Create(nameof(HasSparkline), typeof(bool), typeof(StatTile), false);

    public bool HasSparkline
    {
        get => (bool)GetValue(HasSparklineProperty);
        private set => SetValue(HasSparklineProperty, value);
    }

    public StatTile()
    {
        InitializeComponent();
    }

    private static void OnCaptionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is StatTile tile)
        {
            tile.HasCaption = !string.IsNullOrWhiteSpace(newValue as string);
        }
    }

    private static void OnSparklineChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is StatTile tile)
        {
            // Only show the sparkline when there is at least one real data point to plot.
            tile.HasSparkline = newValue is IEnumerable<double?> values && values.Any(v => v.HasValue);
        }
    }
}
