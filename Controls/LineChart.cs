using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// A trend line for a signed daily series (mood valence, -2..+2). Draws a soft area to the zero
/// baseline, a 2px line, and end dots. The line breaks across days with no data instead of inventing
/// values, and endpoint dates anchor the range. Theme-aware via <see cref="ChartViewBase"/>.
/// </summary>
public class LineChart : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<Diarion.Models.MoodTrendPoint>), typeof(LineChart), null,
            propertyChanged: OnItemsChanged);

    public IEnumerable<Diarion.Models.MoodTrendPoint>? Items
    {
        get => (IEnumerable<Diarion.Models.MoodTrendPoint>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty MinValueProperty =
        BindableProperty.Create(nameof(MinValue), typeof(double), typeof(LineChart), -2.0, propertyChanged: OnVisualChanged);

    public double MinValue
    {
        get => (double)GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public static readonly BindableProperty MaxValueProperty =
        BindableProperty.Create(nameof(MaxValue), typeof(double), typeof(LineChart), 2.0, propertyChanged: OnVisualChanged);

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public static readonly BindableProperty LineColorProperty =
        BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(LineChart), Color.FromArgb("#C26D53"),
            propertyChanged: OnVisualChanged);

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var items = Items?.ToList();
        if (items == null || items.Count == 0) return;

        int count = items.Count;
        float sidePad = 6f;
        float topPad = 10f;
        float labelH = 18f;

        float plotHeight = dirtyRect.Height - topPad - labelH;
        if (plotHeight <= 0) return;
        float plotTop = topPad;
        float plotBottom = topPad + plotHeight;
        float plotWidth = dirtyRect.Width - sidePad * 2;

        double min = MinValue;
        double max = MaxValue;
        double range = max - min;
        if (range <= 0) range = 1;

        float X(int i) => count == 1 ? sidePad + plotWidth / 2f : sidePad + plotWidth * i / (count - 1);
        float Y(double v)
        {
            double clamped = Math.Max(min, Math.Min(max, v));
            return plotBottom - (float)((clamped - min) / range * plotHeight);
        }

        // Zero baseline (only when the range spans zero).
        if (min < 0 && max > 0)
        {
            float zeroY = Y(0);
            canvas.StrokeColor = MutedColor.WithAlpha(0.35f);
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = null;
            canvas.DrawLine(sidePad, zeroY, dirtyRect.Width - sidePad, zeroY);
        }

        float baseY = (min < 0 && max > 0) ? Y(0) : plotBottom;

        // Split into contiguous runs of days that actually have data.
        var runs = new List<List<int>>();
        List<int>? current = null;
        for (int i = 0; i < count; i++)
        {
            if (items[i].HasData)
            {
                current ??= new List<int>();
                current.Add(i);
            }
            else if (current != null)
            {
                runs.Add(current);
                current = null;
            }
        }
        if (current != null) runs.Add(current);

        foreach (var run in runs)
        {
            if (run.Count >= 2)
            {
                // Area between the line and the baseline.
                var area = new PathF();
                area.MoveTo(X(run[0]), baseY);
                foreach (var i in run)
                {
                    area.LineTo(X(i), Y(items[i].Valence));
                }
                area.LineTo(X(run[^1]), baseY);
                area.Close();
                canvas.FillColor = LineColor.WithAlpha(0.12f);
                canvas.FillPath(area);

                // The value line.
                var line = new PathF();
                line.MoveTo(X(run[0]), Y(items[run[0]].Valence));
                foreach (var i in run.Skip(1))
                {
                    line.LineTo(X(i), Y(items[i].Valence));
                }
                canvas.StrokeColor = LineColor;
                canvas.StrokeSize = 2;
                canvas.StrokeLineJoin = LineJoin.Round;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.DrawPath(line);
            }
            else
            {
                // A lone day: a dot, since there is nothing to connect it to.
                int i = run[0];
                DrawDot(canvas, X(i), Y(items[i].Valence), 3.5f);
            }
        }

        // Emphasis dot on the most recent day that has data.
        int lastData = -1;
        for (int i = count - 1; i >= 0; i--)
        {
            if (items[i].HasData) { lastData = i; break; }
        }
        if (lastData >= 0)
        {
            DrawDot(canvas, X(lastData), Y(items[lastData].Valence), 4.5f);
        }

        // Endpoint date labels.
        canvas.FontColor = MutedColor;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 10;
        string first = items[0].Date.ToString("d MMM", CultureInfo.CurrentCulture);
        string lastLabel = items[^1].Date.ToString("d MMM", CultureInfo.CurrentCulture);
        canvas.DrawString(first, new RectF(sidePad, plotBottom + 3, plotWidth / 2f, labelH), HorizontalAlignment.Left, VerticalAlignment.Center);
        canvas.DrawString(lastLabel, new RectF(sidePad + plotWidth / 2f, plotBottom + 3, plotWidth / 2f, labelH), HorizontalAlignment.Right, VerticalAlignment.Center);
    }

    private void DrawDot(ICanvas canvas, float cx, float cy, float radius)
    {
        // Surface ring keeps the dot legible where it overlaps the line/area.
        canvas.FillColor = SurfaceColor;
        canvas.FillCircle(cx, cy, radius + 2f);
        canvas.FillColor = LineColor;
        canvas.FillCircle(cx, cy, radius);
    }
}
