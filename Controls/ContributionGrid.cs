using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// A GitHub-style habit contribution heatmap: 7 rows (Mon–Sun) × week columns over [StartDate, EndDate].
/// A completed day is filled with <see cref="AccentColor"/>, the rest with the recessive track color.
/// Cell size is capped so short ranges don't produce oversized tiles. Theme-aware via <see cref="ChartViewBase"/>.
/// </summary>
public class ContributionGrid : ChartViewBase
{
    public static readonly BindableProperty CompletedDatesProperty =
        BindableProperty.Create(nameof(CompletedDates), typeof(IEnumerable<DateTime>), typeof(ContributionGrid), null,
            propertyChanged: OnVisualChanged);

    public IEnumerable<DateTime>? CompletedDates
    {
        get => (IEnumerable<DateTime>?)GetValue(CompletedDatesProperty);
        set => SetValue(CompletedDatesProperty, value);
    }

    public static readonly BindableProperty StartDateProperty =
        BindableProperty.Create(nameof(StartDate), typeof(DateTime), typeof(ContributionGrid), DateTime.Today, propertyChanged: OnVisualChanged);

    public DateTime StartDate
    {
        get => (DateTime)GetValue(StartDateProperty);
        set => SetValue(StartDateProperty, value);
    }

    public static readonly BindableProperty EndDateProperty =
        BindableProperty.Create(nameof(EndDate), typeof(DateTime), typeof(ContributionGrid), DateTime.Today, propertyChanged: OnVisualChanged);

    public DateTime EndDate
    {
        get => (DateTime)GetValue(EndDateProperty);
        set => SetValue(EndDateProperty, value);
    }

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(ContributionGrid), Color.FromArgb("#8FA083"),
            propertyChanged: OnVisualChanged);

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    private const float Gap = 2.5f;
    private const float MaxCell = 14f;
    private const int Rows = 7;

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var start = StartDate.Date;
        var end = EndDate.Date;
        if (end < start) return;

        var completed = CompletedDates != null
            ? new HashSet<DateTime>(CompletedDates.Select(d => d.Date))
            : new HashSet<DateTime>();

        // Align the first column to the Monday on/before the start date.
        int offset = ((int)start.DayOfWeek + 6) % 7; // Monday = 0 … Sunday = 6
        var alignedStart = start.AddDays(-offset);
        int totalDays = (end - alignedStart).Days + 1;
        int weeks = (totalDays + 6) / 7;
        if (weeks <= 0) return;

        float cellByW = (dirtyRect.Width - Gap * (weeks - 1)) / weeks;
        float cellByH = (dirtyRect.Height - Gap * (Rows - 1)) / Rows;
        float cell = Math.Min(Math.Min(cellByW, cellByH), MaxCell);
        if (cell <= 0) return;

        float step = cell + Gap;
        float gridW = weeks * cell + (weeks - 1) * Gap;
        float gridH = Rows * cell + (Rows - 1) * Gap;
        float left = dirtyRect.X + (dirtyRect.Width - gridW) / 2f;
        float top = dirtyRect.Y + (dirtyRect.Height - gridH) / 2f;

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            int idx = (d - alignedStart).Days;
            int col = idx / 7;
            int row = idx % 7;

            float x = left + col * step;
            float y = top + row * step;

            canvas.FillColor = completed.Contains(d) ? AccentColor : TrackColor;
            canvas.FillRoundedRectangle(x, y, cell, cell, 2);
        }
    }
}
