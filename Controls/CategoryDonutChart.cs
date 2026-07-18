using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class CategoryDonutChart : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<Diarion.Models.CategoryStatItem>), typeof(CategoryDonutChart), null,
            propertyChanged: OnVisualChanged);

    public IEnumerable<Diarion.Models.CategoryStatItem>? Items
    {
        get => (IEnumerable<Diarion.Models.CategoryStatItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty CenterTextProperty =
        BindableProperty.Create(nameof(CenterText), typeof(string), typeof(CategoryDonutChart), string.Empty,
            propertyChanged: OnVisualChanged);

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public static readonly BindableProperty CenterCaptionProperty =
        BindableProperty.Create(nameof(CenterCaption), typeof(string), typeof(CategoryDonutChart), string.Empty,
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
            .Select(i => ((float)i.Percentage, Color.FromArgb(i.ColorHex)))
            .ToList() ?? new List<(float, Color)>();

        DrawDonut(canvas, dirtyRect, segments, drawTrack: segments.Count == 0,
            centerText: string.IsNullOrEmpty(CenterText) ? null : CenterText,
            centerCaption: string.IsNullOrEmpty(CenterCaption) ? null : CenterCaption);
    }
}
