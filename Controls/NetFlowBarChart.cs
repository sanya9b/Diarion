using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.ViewModels;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// Income above a zero line, expense below it, one pair of bars per period bucket. Both halves are scaled
/// to the same peak: scaled independently, a month with 50 000 in and 3 000 out would draw two bars of
/// equal height and the picture would lie about the shape of the month.
/// </summary>
public class NetFlowBarChart : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<DivergingBarChartItem>), typeof(NetFlowBarChart),
            null, propertyChanged: OnItemsChanged);

    public IEnumerable<DivergingBarChartItem>? Items
    {
        get => (IEnumerable<DivergingBarChartItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty IncomeColorProperty =
        BindableProperty.Create(nameof(IncomeColor), typeof(Color), typeof(NetFlowBarChart),
            Color.FromArgb("#8FA083"), propertyChanged: OnVisualChanged);

    public Color IncomeColor
    {
        get => (Color)GetValue(IncomeColorProperty);
        set => SetValue(IncomeColorProperty, value);
    }

    public static readonly BindableProperty ExpenseColorProperty =
        BindableProperty.Create(nameof(ExpenseColor), typeof(Color), typeof(NetFlowBarChart),
            Color.FromArgb("#C26D53"), propertyChanged: OnVisualChanged);

    public Color ExpenseColor
    {
        get => (Color)GetValue(ExpenseColorProperty);
        set => SetValue(ExpenseColorProperty, value);
    }

    /// <summary>
    /// The value both halves scale to. Zero auto-ranges from the bound items, which is the normal case —
    /// the report already computes the peak, so binding it keeps the axis stable while the user flips
    /// between accounts.
    /// </summary>
    public static readonly BindableProperty PeakValueProperty =
        BindableProperty.Create(nameof(PeakValue), typeof(double), typeof(NetFlowBarChart), 0d,
            propertyChanged: OnVisualChanged);

    public double PeakValue
    {
        get => (double)GetValue(PeakValueProperty);
        set => SetValue(PeakValueProperty, value);
    }

    /// <summary>Opacity for buckets clipped by the window, whose bars cover fewer days than their neighbours.</summary>
    public static readonly BindableProperty PartialAlphaProperty =
        BindableProperty.Create(nameof(PartialAlpha), typeof(float), typeof(NetFlowBarChart), 0.45f,
            propertyChanged: OnVisualChanged);

    public float PartialAlpha
    {
        get => (float)GetValue(PartialAlphaProperty);
        set => SetValue(PartialAlphaProperty, value);
    }

    private const float MaxBarWidth = 22f;
    private const float Gap = 6f;

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var items = Items?.ToList();
        if (items == null || items.Count == 0) return;

        const float topPad = 8f;
        const float labelH = 16f;
        const float sidePad = 4f;

        var plotHeight = dirtyRect.Height - topPad - labelH;
        if (plotHeight <= 0) return;

        var half = plotHeight / 2f;
        var midY = topPad + half;
        var availableWidth = dirtyRect.Width - sidePad * 2;
        if (availableWidth <= 0) return;

        var slot = availableWidth / items.Count;
        var barWidth = Math.Max(1f, Math.Min(MaxBarWidth, slot - Gap));

        var peak = PeakValue > 0
            ? PeakValue
            : items.Select(i => Math.Max(i.Income, i.Expense)).DefaultIfEmpty(0d).Max();
        if (peak <= 0) peak = 1d;

        canvas.StrokeColor = MutedColor.WithAlpha(0.35f);
        canvas.StrokeSize = 1;
        canvas.StrokeLineCap = LineCap.Butt;
        canvas.DrawLine(sidePad, midY, dirtyRect.Width - sidePad, midY);

        // With many buckets every label would collide, so thin them out rather than shrink the type.
        var labelEvery = items.Count <= 7 ? 1 : (int)Math.Ceiling(items.Count / 7.0);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var centerX = sidePad + slot * i + slot / 2f;

            if (i % labelEvery == 0)
            {
                canvas.FontColor = MutedColor;
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.FontSize = 10;
                canvas.DrawString(item.Label, new RectF(centerX - slot / 2f, midY + half + 2, slot, labelH),
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }

            var alpha = item.IsPartial ? PartialAlpha : 1f;
            var barLeft = centerX - barWidth / 2f;

            if (item.Income > 0)
            {
                var h = Math.Max(2f, (float)(item.Income / peak * half));
                canvas.FillColor = IncomeColor.WithAlpha(alpha);
                canvas.FillRoundedRectangle(barLeft, midY - h, barWidth, h, 4, 4, 0, 0);
            }

            if (item.Expense > 0)
            {
                var h = Math.Max(2f, (float)(item.Expense / peak * half));
                canvas.FillColor = ExpenseColor.WithAlpha(alpha);
                canvas.FillRoundedRectangle(barLeft, midY, barWidth, h, 0, 0, 4, 4);
            }
        }
    }
}
