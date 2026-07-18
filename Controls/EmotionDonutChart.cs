using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class EmotionDonutChart : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<Diarion.ViewModels.EmotionChartItem>), typeof(EmotionDonutChart), null,
            propertyChanged: OnVisualChanged);

    public IEnumerable<Diarion.ViewModels.EmotionChartItem>? Items
    {
        get => (IEnumerable<Diarion.ViewModels.EmotionChartItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty CenterTextProperty =
        BindableProperty.Create(nameof(CenterText), typeof(string), typeof(EmotionDonutChart), string.Empty,
            propertyChanged: OnVisualChanged);

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public static readonly BindableProperty CenterCaptionProperty =
        BindableProperty.Create(nameof(CenterCaption), typeof(string), typeof(EmotionDonutChart), string.Empty,
            propertyChanged: OnVisualChanged);

    public string CenterCaption
    {
        get => (string)GetValue(CenterCaptionProperty);
        set => SetValue(CenterCaptionProperty, value);
    }

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var segments = Items?
            .Where(i => i.Percentage > 0)
            .Select(i => ((float)i.Percentage, i.Color))
            .ToList() ?? new List<(float, Color)>();

        DrawDonut(canvas, dirtyRect, segments, drawTrack: segments.Count == 0,
            centerText: string.IsNullOrEmpty(CenterText) ? null : CenterText,
            centerCaption: string.IsNullOrEmpty(CenterCaption) ? null : CenterCaption);
    }
}
