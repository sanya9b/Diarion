using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// A "Year in Pixels" mood heatmap: a months (columns) × day-of-month (rows) calendar grid where each
/// day is a small tile colored by its dominant emotion. Days with no logged mood use the track color.
/// Cell size is capped so short windows don't produce giant tiles. Theme-aware via <see cref="ChartViewBase"/>.
/// </summary>
public class YearInPixelsView : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<Diarion.Models.MoodHeatDay>), typeof(YearInPixelsView), null,
            propertyChanged: OnItemsChanged);

    public IEnumerable<Diarion.Models.MoodHeatDay>? Items
    {
        get => (IEnumerable<Diarion.Models.MoodHeatDay>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    private const float Gap = 2f;
    private const float MaxCell = 16f;
    private const float LabelH = 16f;
    private const int Rows = 31;

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var items = Items?.ToList();
        if (items == null || items.Count == 0) return;

        // Columns are the distinct months present in the window, in chronological order.
        var columnKeys = items
            .Select(i => new DateTime(i.Date.Year, i.Date.Month, 1))
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        int cols = columnKeys.Count;
        if (cols == 0) return;
        var columnIndex = columnKeys.Select((k, idx) => (k, idx)).ToDictionary(x => x.k, x => x.idx);

        float availW = dirtyRect.Width;
        float availH = dirtyRect.Height - LabelH;
        if (availW <= 0 || availH <= 0) return;

        float cellByW = (availW - Gap * (cols - 1)) / cols;
        float cellByH = (availH - Gap * (Rows - 1)) / Rows;
        float cell = Math.Min(Math.Min(cellByW, cellByH), MaxCell);
        if (cell <= 0) return;

        float step = cell + Gap;
        float gridW = cols * step - Gap;
        float gridH = Rows * step - Gap;
        float left = dirtyRect.X + (availW - gridW) / 2f;
        float top = dirtyRect.Y + (availH - gridH) / 2f;

        foreach (var item in items)
        {
            if (!columnIndex.TryGetValue(new DateTime(item.Date.Year, item.Date.Month, 1), out int col)) continue;
            int row = item.Date.Day - 1;
            if (row < 0 || row >= Rows) continue;

            float x = left + col * step;
            float y = top + row * step;

            canvas.FillColor = item.HasData ? Color.FromArgb(item.ColorHex) : TrackColor;
            canvas.FillRoundedRectangle(x, y, cell, cell, 2);
        }

        // Month labels under each column.
        canvas.FontColor = MutedColor;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 10;
        float labelY = top + gridH + 2;
        foreach (var key in columnKeys)
        {
            int col = columnIndex[key];
            float x = left + col * step;
            string label = key.ToString("MMM", CultureInfo.CurrentCulture);
            canvas.DrawString(label, new RectF(x, labelY, cell, LabelH), HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
