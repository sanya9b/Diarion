using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class ProgressDonutChart : ChartViewBase
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressDonutChart), 0.0,
            propertyChanged: OnVisualChanged);

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly BindableProperty ProgressColorProperty =
        BindableProperty.Create(nameof(ProgressColor), typeof(Color), typeof(ProgressDonutChart), Color.FromArgb("#8FA083"),
            propertyChanged: OnVisualChanged);

    public Color ProgressColor
    {
        get => (Color)GetValue(ProgressColorProperty);
        set => SetValue(ProgressColorProperty, value);
    }

    public static readonly BindableProperty CenterTextProperty =
        BindableProperty.Create(nameof(CenterText), typeof(string), typeof(ProgressDonutChart), string.Empty,
            propertyChanged: OnVisualChanged);

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public static readonly BindableProperty CenterCaptionProperty =
        BindableProperty.Create(nameof(CenterCaption), typeof(string), typeof(ProgressDonutChart), string.Empty,
            propertyChanged: OnVisualChanged);

    public string CenterCaption
    {
        get => (string)GetValue(CenterCaptionProperty);
        set => SetValue(CenterCaptionProperty, value);
    }

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        double clamped = Progress < 0 ? 0 : (Progress > 1 ? 1 : Progress);

        // Single arc over a full track; rounded leading cap for a softer, modern look.
        var segments = new List<(float, Color)> { ((float)clamped, ProgressColor) };

        DrawDonut(canvas, dirtyRect, segments, drawTrack: true,
            centerText: string.IsNullOrEmpty(CenterText) ? null : CenterText,
            centerCaption: string.IsNullOrEmpty(CenterCaption) ? null : CenterCaption,
            roundCap: true);
    }
}
