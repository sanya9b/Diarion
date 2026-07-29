using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// Average mood valence per hour of day, as bars diverging from a mid baseline: above the line for
/// hours that skew positive, below for hours that skew negative. Hours with nothing logged are left
/// blank rather than drawn at zero height, which would read as "neutral" instead of "no data".
/// </summary>
public class MoodHourBarChart : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<MoodHourPoint>), typeof(MoodHourBarChart), null,
            propertyChanged: OnItemsChanged);

    public IEnumerable<MoodHourPoint>? Items
    {
        get => (IEnumerable<MoodHourPoint>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty PositiveColorProperty =
        BindableProperty.Create(nameof(PositiveColor), typeof(Color), typeof(MoodHourBarChart), Color.FromArgb("#8FA083"),
            propertyChanged: OnVisualChanged);

    public Color PositiveColor
    {
        get => (Color)GetValue(PositiveColorProperty);
        set => SetValue(PositiveColorProperty, value);
    }

    public static readonly BindableProperty NegativeColorProperty =
        BindableProperty.Create(nameof(NegativeColor), typeof(Color), typeof(MoodHourBarChart), Color.FromArgb("#C26D53"),
            propertyChanged: OnVisualChanged);

    public Color NegativeColor
    {
        get => (Color)GetValue(NegativeColorProperty);
        set => SetValue(NegativeColorProperty, value);
    }

    public static readonly BindableProperty MaxValueProperty =
        BindableProperty.Create(nameof(MaxValue), typeof(double), typeof(MoodHourBarChart), 2.0,
            propertyChanged: OnVisualChanged);

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>Below this many observations an hour is drawn faded, so a one-off entry cannot pass for a pattern.</summary>
    public static readonly BindableProperty SparseThresholdProperty =
        BindableProperty.Create(nameof(SparseThreshold), typeof(int), typeof(MoodHourBarChart), 3,
            propertyChanged: OnVisualChanged);

    public int SparseThreshold
    {
        get => (int)GetValue(SparseThresholdProperty);
        set => SetValue(SparseThresholdProperty, value);
    }

    public static readonly BindableProperty SparseAlphaProperty =
        BindableProperty.Create(nameof(SparseAlpha), typeof(float), typeof(MoodHourBarChart), 0.35f,
            propertyChanged: OnVisualChanged);

    public float SparseAlpha
    {
        get => (float)GetValue(SparseAlphaProperty);
        set => SetValue(SparseAlphaProperty, value);
    }

    private const float MaxBarWidth = 18f;
    private const float Gap = 2.5f;

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var items = Items?.ToList();
        if (items == null || items.Count == 0) return;

        float topPad = 10f;
        float labelH = 18f;
        float sidePad = 4f;

        float plotHeight = dirtyRect.Height - topPad - labelH;
        if (plotHeight <= 0) return;

        float half = plotHeight / 2f;
        float midY = topPad + half;
        float availableWidth = dirtyRect.Width - sidePad * 2;
        if (availableWidth <= 0) return;

        float slot = availableWidth / items.Count;
        float barWidth = Math.Max(1f, Math.Min(MaxBarWidth, slot - Gap));

        double max = MaxValue <= 0 ? 1 : MaxValue;

        // Zero baseline (recessive hairline).
        canvas.StrokeColor = MutedColor.WithAlpha(0.35f);
        canvas.StrokeSize = 1;
        canvas.StrokeLineCap = LineCap.Butt;
        canvas.DrawLine(sidePad, midY, dirtyRect.Width - sidePad, midY);

        for (int i = 0; i < items.Count; i++)
        {
            var point = items[i];
            float centerX = sidePad + slot * i + slot / 2f;

            // Hour labels every four hours; seventeen of them would collide.
            if (point.Hour % 4 == 0)
            {
                canvas.FontColor = MutedColor;
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.FontSize = 10;
                var labelRect = new RectF(centerX - slot / 2f, midY + half + 3, slot, labelH);
                canvas.DrawString(point.Hour.ToString("00", CultureInfo.InvariantCulture), labelRect,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }

            // Nothing logged: leave the slot blank, but keep its position so the axis stays even.
            if (!point.HasData) continue;

            double value = Math.Clamp(point.Valence, -max, max);
            var color = value > 0 ? PositiveColor : value < 0 ? NegativeColor : MutedColor;
            if (point.Count < SparseThreshold) color = color.WithAlpha(SparseAlpha);

            canvas.FillColor = color;
            float barLeft = centerX - barWidth / 2f;

            // Logged, but the hours cancel out exactly. A bar in either direction would be a lie, and
            // skipping it would claim there is no data, so mark it with a tick straddling the baseline.
            if (Math.Abs(value) < 0.001)
            {
                canvas.FillRectangle(barLeft, midY - 1f, barWidth, 2f);
                continue;
            }

            float barHeight = Math.Max(3f, (float)(Math.Abs(value) / max * half));

            if (value > 0)
            {
                canvas.FillRoundedRectangle(barLeft, midY - barHeight, barWidth, barHeight, 4, 4, 0, 0);
            }
            else
            {
                canvas.FillRoundedRectangle(barLeft, midY, barWidth, barHeight, 0, 0, 4, 4);
            }
        }
    }
}
