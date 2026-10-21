using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Diarion.Models;
using Diarion.Resources.Localization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// The days of a window and which of them were written on, for the "days with entries" card. Two shapes,
/// chosen by length, because no single one reads well at both ends:
/// <list type="bullet">
/// <item>Up to six weeks: a calendar — seven columns, Monday first, a week per row, the day's number in
/// its square. A month at a glance, the way a person already reads one.</item>
/// <item>Longer: weeks run left to right as narrow columns of seven, so half a year or a year still fits
/// the card's width; month names along the bottom.</item>
/// </list>
/// The view sizes its own height from its width (a GraphicsView cannot measure its drawing), so the card
/// is exactly as tall as the grid. Written days take <see cref="ActiveColor"/>; the rest the track colour;
/// today is ringed.
/// </summary>
public class ActivityGridView : ChartViewBase
{
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.Create(nameof(Items), typeof(IEnumerable<MoodHeatDay>), typeof(ActivityGridView), null,
            propertyChanged: OnActivityItemsChanged);

    public IEnumerable<MoodHeatDay>? Items
    {
        get => (IEnumerable<MoodHeatDay>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly BindableProperty ActiveColorProperty =
        BindableProperty.Create(nameof(ActiveColor), typeof(Color), typeof(ActivityGridView), Colors.Coral, propertyChanged: OnVisualChanged);

    /// <summary>A written day.</summary>
    public Color ActiveColor
    {
        get => (Color)GetValue(ActiveColorProperty);
        set => SetValue(ActiveColorProperty, value);
    }

    /// <summary>Longest a window can be and still be drawn as a calendar.</summary>
    private const int CalendarMaxWeeks = 6;

    private const float Gap = 4f;
    private const float CalendarMaxCell = 38f;
    private const float StripMaxCell = 16f;
    private const float StripGap = 2f;
    private const float LabelH = 16f;

    private static void OnActivityItemsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        OnItemsChanged(bindable, oldValue, newValue);
        ((ActivityGridView)bindable).InvalidateMeasure();
    }

    private readonly record struct Shape(bool IsCalendar, DateTime FirstMonday, int Weeks, float Cell, float Height);

    private Shape? Layout(double width)
    {
        var items = Items?.ToList();
        if (items is not { Count: > 0 } || width <= 0) return null;

        var first = items.Min(i => i.Date.Date);
        var last = items.Max(i => i.Date.Date);
        var firstMonday = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var weeks = (int)((last - firstMonday).TotalDays / 7) + 1;
        var w = (float)width;

        if (weeks <= CalendarMaxWeeks)
        {
            var cell = Math.Min((w - Gap * 6) / 7f, CalendarMaxCell);
            return new Shape(true, firstMonday, weeks, cell, LabelH + weeks * cell + (weeks - 1) * Gap);
        }

        var stripCell = Math.Min((w - StripGap * (weeks - 1)) / weeks, StripMaxCell);
        return new Shape(false, firstMonday, weeks, stripCell, 7 * stripCell + 6 * StripGap + LabelH);
    }

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var width = double.IsFinite(widthConstraint) ? widthConstraint : Width;
        var shape = Layout(width);
        return new Size(width > 0 ? width : 0, shape?.Height ?? 0);
    }

    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;
        if (Layout(dirtyRect.Width) is not Shape shape) return;

        var days = Items!.ToDictionary(i => i.Date.Date, i => i.HasData);
        if (shape.IsCalendar) DrawCalendar(canvas, dirtyRect, shape, days);
        else DrawStrip(canvas, dirtyRect, shape, days);
    }

    private void DrawCalendar(ICanvas canvas, RectF rect, Shape shape, Dictionary<DateTime, bool> days)
    {
        var cell = shape.Cell;
        var gridW = 7 * cell + 6 * Gap;
        var left = rect.X + (rect.Width - gridW) / 2f;

        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 10;
        canvas.FontColor = MutedColor;
        string[] weekdays =
        {
            AppResources.WeekdayMonShort, AppResources.WeekdayTueShort, AppResources.WeekdayWedShort,
            AppResources.WeekdayThuShort, AppResources.WeekdayFriShort, AppResources.WeekdaySatShort,
            AppResources.WeekdaySunShort
        };
        for (var c = 0; c < 7; c++)
            canvas.DrawString(weekdays[c], new RectF(left + c * (cell + Gap), rect.Y, cell, LabelH),
                HorizontalAlignment.Center, VerticalAlignment.Top);

        var top = rect.Y + LabelH;
        canvas.FontSize = Math.Min(12, cell * 0.36f);
        for (var week = 0; week < shape.Weeks; week++)
        {
            for (var dow = 0; dow < 7; dow++)
            {
                var date = shape.FirstMonday.AddDays(week * 7 + dow);
                if (!days.TryGetValue(date, out var written)) continue;

                var x = left + dow * (cell + Gap);
                var y = top + week * (cell + Gap);
                canvas.FillColor = written ? ActiveColor : TrackColor;
                canvas.FillRoundedRectangle(x, y, cell, cell, cell * 0.28f);
                MarkToday(canvas, date, x, y, cell, cell * 0.28f);

                canvas.FontColor = written ? Colors.White : MutedColor;
                canvas.DrawString(date.Day.ToString(CultureInfo.CurrentCulture), new RectF(x, y, cell, cell),
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }

    private void DrawStrip(ICanvas canvas, RectF rect, Shape shape, Dictionary<DateTime, bool> days)
    {
        var cell = shape.Cell;
        var gridW = shape.Weeks * cell + (shape.Weeks - 1) * StripGap;
        var left = rect.X + (rect.Width - gridW) / 2f;
        var top = rect.Y;

        for (var week = 0; week < shape.Weeks; week++)
        {
            for (var dow = 0; dow < 7; dow++)
            {
                var date = shape.FirstMonday.AddDays(week * 7 + dow);
                if (!days.TryGetValue(date, out var written)) continue;

                var x = left + week * (cell + StripGap);
                var y = top + dow * (cell + StripGap);
                canvas.FillColor = written ? ActiveColor : TrackColor;
                canvas.FillRoundedRectangle(x, y, cell, cell, 2);
                MarkToday(canvas, date, x, y, cell, 2);
            }
        }

        // A month name under the first week that starts in it.
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 10;
        canvas.FontColor = MutedColor;
        var labelY = top + 7 * cell + 6 * StripGap + 2;
        var lastMonth = -1;
        for (var week = 0; week < shape.Weeks; week++)
        {
            var monday = shape.FirstMonday.AddDays(week * 7);
            if (monday.Month == lastMonth) continue;
            lastMonth = monday.Month;
            var x = left + week * (cell + StripGap);
            canvas.DrawString(monday.ToString("MMM", CultureInfo.CurrentCulture), new RectF(x, labelY, 40, LabelH - 2),
                HorizontalAlignment.Left, VerticalAlignment.Top);
        }
    }

    private void MarkToday(ICanvas canvas, DateTime date, float x, float y, float cell, float radius)
    {
        if (date != DateTime.Today) return;
        canvas.StrokeColor = TextColor;
        canvas.StrokeSize = 1.5f;
        canvas.DrawRoundedRectangle(x + 0.75f, y + 0.75f, cell - 1.5f, cell - 1.5f, radius);
    }
}
