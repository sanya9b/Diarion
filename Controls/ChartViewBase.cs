using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// Base class for the statistics charts. Exposes theme-aware bindable colors (fed from the
/// <c>Theme_*</c> resources via <c>DynamicResource</c> in XAML) so charts follow the Light / Dark /
/// Pink themes and repaint on theme switch, instead of reading <see cref="Application.UserAppTheme"/>
/// directly. Also provides a shared, polished donut renderer with surface-color gaps between segments.
/// </summary>
public abstract class ChartViewBase : GraphicsView, IDrawable
{
    /// <summary>Card/background color the chart sits on. The gaps between donut segments show this through.</summary>
    public static readonly BindableProperty SurfaceColorProperty =
        BindableProperty.Create(nameof(SurfaceColor), typeof(Color), typeof(ChartViewBase), Colors.White, propertyChanged: OnVisualChanged);

    public Color SurfaceColor
    {
        get => (Color)GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
    }

    /// <summary>Primary text/ink color for center values and labels.</summary>
    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(ChartViewBase), Colors.Black, propertyChanged: OnVisualChanged);

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    /// <summary>Recessive track color (empty rings, progress remainder, baselines).</summary>
    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(ChartViewBase), Color.FromArgb("#22929FA7"), propertyChanged: OnVisualChanged);

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    /// <summary>Muted color for secondary labels and reference lines.</summary>
    public static readonly BindableProperty MutedColorProperty =
        BindableProperty.Create(nameof(MutedColor), typeof(Color), typeof(ChartViewBase), Color.FromArgb("#929FA7"), propertyChanged: OnVisualChanged);

    public Color MutedColor
    {
        get => (Color)GetValue(MutedColorProperty);
        set => SetValue(MutedColorProperty, value);
    }

    protected ChartViewBase()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;

        // A GraphicsView bound before its platform view exists / before it has a size can miss its first
        // (and only) draw and render blank. Re-invalidate once it's realized and whenever it's resized.
        Loaded += (_, _) => Invalidate();
        SizeChanged += (_, _) => Invalidate();
    }

    protected static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ChartViewBase)bindable).Invalidate();

    /// <summary>
    /// <c>propertyChanged</c> handler for collection-typed bindable properties (the various <c>Items</c> /
    /// <c>Values</c> / <c>CompletedDates</c>). A plain <see cref="IEnumerable{T}"/> bindable property is set
    /// once at bind time and does NOT react to in-place mutation of the bound <c>ObservableCollection</c>
    /// (the view models <c>Clear()</c> then <c>Add()</c> the same instance), so a chart bound before its data
    /// loads asynchronously would draw empty and never repaint. This subscribes to
    /// <see cref="INotifyCollectionChanged"/> on the bound collection — unsubscribing the previous one — so
    /// the chart repaints whenever its data arrives or changes. Reassigning the collection still repaints via
    /// the trailing <see cref="GraphicsView.Invalidate"/>.
    /// </summary>
    protected static void OnItemsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var chart = (ChartViewBase)bindable;
        if (oldValue is INotifyCollectionChanged oldCollection)
            oldCollection.CollectionChanged -= chart.OnItemsCollectionChanged;
        if (newValue is INotifyCollectionChanged newCollection)
            newCollection.CollectionChanged += chart.OnItemsCollectionChanged;
        chart.Invalidate();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();

    public abstract void Draw(ICanvas canvas, RectF dirtyRect);

    /// <summary>
    /// Draws a polished donut ring. Segments are separated by a ~2px gap rendered in the surface color
    /// (transparent gap reveals the card behind), which is the secondary encoding the calming palette
    /// needs to stay legible. Optionally draws a recessive full track (for progress remainders) and a
    /// centered value with an optional caption line.
    /// </summary>
    protected void DrawDonut(
        ICanvas canvas,
        RectF rect,
        IReadOnlyList<(float Fraction, Color Color)> segments,
        bool drawTrack,
        string? centerText = null,
        string? centerCaption = null,
        bool roundCap = false)
    {
        canvas.Antialias = true;

        var center = new PointF(rect.Center.X, rect.Center.Y);
        float radius = Math.Min(rect.Width, rect.Height) / 2f - 6f;
        if (radius <= 0) return;

        float thickness = radius * 0.26f;
        float drawRadius = radius - thickness / 2f;
        float x = center.X - drawRadius;
        float y = center.Y - drawRadius;
        float size = drawRadius * 2f;

        // Count segments that actually render, to decide whether inter-segment gaps are needed.
        int positive = 0;
        foreach (var s in segments)
        {
            if (s.Fraction > 0.0001f) positive++;
        }

        // A 2px arc-gap converted to degrees at the draw radius (only when there is more than one slice).
        float gapDeg = positive > 1 ? (2f / drawRadius) * 180f / MathF.PI : 0f;

        if (drawTrack || positive == 0)
        {
            canvas.StrokeColor = TrackColor;
            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = LineCap.Butt;
            canvas.DrawArc(x, y, size, size, 0, 360, true, false);
        }

        float start = 90f; // 12 o'clock, sweeping clockwise
        foreach (var seg in segments)
        {
            float fullSweep = seg.Fraction * 360f;
            if (fullSweep <= 0.0001f) continue;

            float drawnSweep = Math.Max(fullSweep - gapDeg, 1f);
            float end = start - drawnSweep;

            canvas.StrokeColor = seg.Color;
            canvas.StrokeSize = thickness;
            canvas.StrokeLineCap = roundCap ? LineCap.Round : LineCap.Butt;
            canvas.DrawArc(x, y, size, size, start, end, true, false);

            start -= fullSweep;
        }

        DrawCenterText(canvas, rect, radius, centerText, centerCaption);
    }

    protected void DrawCenterText(ICanvas canvas, RectF rect, float radius, string? centerText, string? centerCaption)
    {
        if (string.IsNullOrEmpty(centerText)) return;

        bool hasCaption = !string.IsNullOrEmpty(centerCaption);
        float valueSize = radius * 0.42f;
        float captionSize = radius * 0.20f;

        canvas.FontColor = TextColor;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = valueSize;

        if (hasCaption)
        {
            var valueRect = new RectF(rect.X, rect.Center.Y - valueSize, rect.Width, valueSize + 2);
            canvas.DrawString(centerText, valueRect, HorizontalAlignment.Center, VerticalAlignment.Center);

            canvas.FontColor = MutedColor;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = captionSize;
            var captionRect = new RectF(rect.X, rect.Center.Y + 2, rect.Width, captionSize + 4);
            canvas.DrawString(centerCaption, captionRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
        else
        {
            canvas.DrawString(centerText, rect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
