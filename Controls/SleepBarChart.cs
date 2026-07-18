using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

public class SleepBarChart : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<Diarion.ViewModels.SleepBarChartItem>), typeof(SleepBarChart), null,
            propertyChanged: OnVisualChanged);

    public IEnumerable<Diarion.ViewModels.SleepBarChartItem>? Items
    {
        get => (IEnumerable<Diarion.ViewModels.SleepBarChartItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty MaxValueProperty =
        BindableProperty.Create(nameof(MaxValue), typeof(double), typeof(SleepBarChart), 12.0,
            propertyChanged: OnVisualChanged);

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public static readonly BindableProperty BarColorProperty =
        BindableProperty.Create(nameof(BarColor), typeof(Color), typeof(SleepBarChart), Color.FromArgb("#C9985A"),
            propertyChanged: OnVisualChanged);

    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    private const float MaxBarWidth = 24f;

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var items = Items?.ToList();
        if (items == null || items.Count == 0) return;

        int count = items.Count;

        float topPad = 18f;    // room for value labels above caps
        float labelH = 18f;    // day labels under the baseline
        float sidePad = 4f;

        float graphHeight = dirtyRect.Height - labelH - topPad;
        if (graphHeight <= 0) return;
        float baselineY = topPad + graphHeight;
        float availableWidth = dirtyRect.Width - sidePad * 2;

        float slot = availableWidth / count;
        float barWidth = Math.Min(MaxBarWidth, slot * 0.6f);

        double max = MaxValue <= 0 ? 1 : MaxValue;

        // Baseline (recessive hairline).
        canvas.StrokeColor = MutedColor.WithAlpha(0.35f);
        canvas.StrokeSize = 1;
        canvas.StrokeLineCap = LineCap.Butt;
        canvas.DrawLine(sidePad, baselineY, dirtyRect.Width - sidePad, baselineY);

        // Average reference line across the populated days.
        var populated = items.Where(i => i.Value > 0).ToList();
        if (populated.Count > 1)
        {
            double avg = populated.Average(i => i.Value);
            float avgY = baselineY - (float)(Math.Min(avg, max) / max * graphHeight);

            canvas.StrokeColor = MutedColor.WithAlpha(0.6f);
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = new float[] { 4, 3 };
            canvas.DrawLine(sidePad, avgY, dirtyRect.Width - sidePad, avgY);
            canvas.StrokeDashPattern = null; // reset to solid for any subsequent strokes
        }

        bool showValues = count <= 10;

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            double val = Math.Min(item.Value, max);
            float barHeight = (float)(val / max * graphHeight);
            if (item.Value > 0 && barHeight < 3) barHeight = 3;

            float centerX = sidePad + slot * i + slot / 2f;
            float barLeft = centerX - barWidth / 2f;
            float barTop = baselineY - barHeight;

            if (barHeight > 0)
            {
                canvas.FillColor = BarColor;
                // Rounded top corners, square base sitting on the baseline.
                canvas.FillRoundedRectangle(barLeft, barTop, barWidth, barHeight, 4, 4, 0, 0);
            }

            // Value label above the cap.
            if (showValues && item.Value > 0)
            {
                canvas.FontColor = TextColor;
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                canvas.FontSize = 10;
                var valueRect = new RectF(centerX - slot / 2f, barTop - topPad, slot, topPad);
                canvas.DrawString(item.Value.ToString("0.#", CultureInfo.CurrentCulture), valueRect,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }

            // Day / group label below the baseline.
            canvas.FontColor = MutedColor;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 10;
            var labelRect = new RectF(centerX - slot / 2f, baselineY + 3, slot, labelH);
            canvas.DrawString(item.Label, labelRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
