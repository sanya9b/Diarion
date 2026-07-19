using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// A tiny, axis-less trend line for a KPI tile. Auto-ranges to the data, draws a soft area + a thin
/// line, and marks the last point. Values are nullable — a null is a gap (missing day) and breaks the
/// line rather than inventing a value. Theme-aware via <see cref="ChartViewBase"/>.
/// </summary>
public class Sparkline : ChartViewBase
{
    public static readonly BindableProperty ValuesProperty =
        BindableProperty.Create(nameof(Values), typeof(IEnumerable<double?>), typeof(Sparkline), null,
            propertyChanged: OnItemsChanged);

    public IEnumerable<double?>? Values
    {
        get => (IEnumerable<double?>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public static readonly BindableProperty LineColorProperty =
        BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(Sparkline), Color.FromArgb("#929FA7"),
            propertyChanged: OnVisualChanged);

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var list = Values?.ToList();
        if (list == null || list.Count == 0) return;

        var present = list.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (present.Count == 0) return;

        double min = present.Min();
        double max = present.Max();
        if (max - min < 1e-9) { min -= 1; max += 1; } // flat series -> center it
        double range = max - min;

        float pad = 3f;
        float plotH = dirtyRect.Height - pad * 2;
        float plotW = dirtyRect.Width - pad * 2;
        if (plotH <= 0 || plotW <= 0) return;
        float bottom = pad + plotH;

        int count = list.Count;
        float X(int i) => count == 1 ? pad + plotW / 2f : pad + plotW * i / (count - 1);
        float Y(double v) => bottom - (float)((v - min) / range * plotH);

        // Contiguous runs of days that have data.
        var runs = new List<List<int>>();
        List<int>? current = null;
        for (int i = 0; i < count; i++)
        {
            if (list[i].HasValue)
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
                var area = new PathF();
                area.MoveTo(X(run[0]), bottom);
                foreach (var i in run) area.LineTo(X(i), Y(list[i]!.Value));
                area.LineTo(X(run[^1]), bottom);
                area.Close();
                canvas.FillColor = LineColor.WithAlpha(0.14f);
                canvas.FillPath(area);

                var line = new PathF();
                line.MoveTo(X(run[0]), Y(list[run[0]]!.Value));
                foreach (var i in run.Skip(1)) line.LineTo(X(i), Y(list[i]!.Value));
                canvas.StrokeColor = LineColor;
                canvas.StrokeSize = 1.5f;
                canvas.StrokeLineJoin = LineJoin.Round;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.DrawPath(line);
            }
            else
            {
                int i = run[0];
                canvas.FillColor = LineColor;
                canvas.FillCircle(X(i), Y(list[i]!.Value), 2f);
            }
        }

        // Emphasis dot on the most recent day with data.
        int lastData = -1;
        for (int i = count - 1; i >= 0; i--)
        {
            if (list[i].HasValue) { lastData = i; break; }
        }
        if (lastData >= 0)
        {
            float cx = X(lastData);
            float cy = Y(list[lastData]!.Value);
            canvas.FillColor = SurfaceColor;
            canvas.FillCircle(cx, cy, 4f);
            canvas.FillColor = LineColor;
            canvas.FillCircle(cx, cy, 2.5f);
        }
    }
}
