using System;
using Diarion.Resources.Styles;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// Fourteen notches and a bookmark (spec 17, R2.2). A notch is a paired day that counted, never a
/// calendar day — a missed day is not drawn at all, so the strip cannot shame anyone. The bookmark
/// stands over the last notch that counted, or at the very start before any has. When the count
/// grows the fill advances and the bookmark slides, on the motion tokens; under reduce motion both
/// simply move.
/// </summary>
public class InsightPathStrip : GraphicsView, IDrawable
{
    public static readonly BindableProperty FilledProperty =
        BindableProperty.Create(nameof(Filled), typeof(int), typeof(InsightPathStrip), 0, propertyChanged: OnFilledChanged);

    public int Filled
    {
        get => (int)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public static readonly BindableProperty TotalProperty =
        BindableProperty.Create(nameof(Total), typeof(int), typeof(InsightPathStrip), 14, propertyChanged: OnVisualChanged);

    public int Total
    {
        get => (int)GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    /// <summary>An empty notch.</summary>
    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(InsightPathStrip), Color.FromArgb("#22929FA7"), propertyChanged: OnVisualChanged);

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    /// <summary>A notch that counted.</summary>
    public static readonly BindableProperty InkColorProperty =
        BindableProperty.Create(nameof(InkColor), typeof(Color), typeof(InsightPathStrip), Colors.Black, propertyChanged: OnVisualChanged);

    public Color InkColor
    {
        get => (Color)GetValue(InkColorProperty);
        set => SetValue(InkColorProperty, value);
    }

    /// <summary>The bookmark.</summary>
    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(InsightPathStrip), Color.FromArgb("#C26D53"), propertyChanged: OnVisualChanged);

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    private const float BookmarkWidth = 8f;
    private const float BookmarkHeight = BookmarkWidth * 80f / 44f;
    private const float NotchHeight = 6f;
    private const float NotchGap = 3f;
    private const float NotchRadius = 3f;
    private const float BookmarkToNotch = 3f;

    /// <summary>The height the strip needs: bookmark, a breath, notches.</summary>
    public const double PreferredHeight = BookmarkHeight + BookmarkToNotch + NotchHeight;

    // The count as currently drawn; lags Filled while the fill animates.
    private double _shown;

    public InsightPathStrip()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        HeightRequest = PreferredHeight;
        Loaded += (_, _) => Invalidate();
        SizeChanged += (_, _) => Invalidate();
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
        => ((InsightPathStrip)bindable).Invalidate();

    private static void OnFilledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var strip = (InsightPathStrip)bindable;
        var target = (int)newValue;

        // The first value is the state the card loads with, not a change worth animating.
        if (oldValue is int previous && previous == 0 && strip._shown == 0 && !strip.IsLoaded)
        {
            strip._shown = target;
            strip.Invalidate();
            return;
        }

        if (Motion.ReduceMotion)
        {
            strip._shown = target;
            strip.Invalidate();
            return;
        }

        strip.AbortAnimation(nameof(InsightPathStrip));
        new Animation(v => { strip._shown = v; strip.Invalidate(); }, strip._shown, target)
            .Commit(strip, nameof(InsightPathStrip), length: (uint)Motion.StandardMs, easing: Motion.EaseOut);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var total = Math.Max(1, Total);
        var width = dirtyRect.Width;
        if (width <= 0)
            return;

        canvas.SaveState();
        canvas.Antialias = true;

        var notchWidth = (width - NotchGap * (total - 1)) / total;
        var notchTop = dirtyRect.Y + BookmarkHeight + BookmarkToNotch;
        var shown = Math.Clamp(_shown, 0, total);

        for (var i = 0; i < total; i++)
        {
            var x = dirtyRect.X + i * (notchWidth + NotchGap);

            canvas.FillColor = TrackColor;
            canvas.FillRoundedRectangle(x, notchTop, notchWidth, NotchHeight, NotchRadius);

            // A whole notch for each day that counted, and a growing sliver for the one mid-animation.
            var fraction = Math.Clamp(shown - i, 0, 1);
            if (fraction > 0)
            {
                canvas.FillColor = InkColor;
                canvas.FillRoundedRectangle(x, notchTop, (float)(notchWidth * fraction), NotchHeight, NotchRadius);
            }
        }

        // Over the right end of the last notch that counted; at the start before any has.
        var lastEnd = shown <= 0
            ? dirtyRect.X
            : dirtyRect.X + (float)(Math.Ceiling(shown) - 1) * (notchWidth + NotchGap) + (float)(notchWidth * Math.Clamp(shown - (Math.Ceiling(shown) - 1), 0, 1));
        var bookmarkX = Math.Clamp(lastEnd - BookmarkWidth / 2f, dirtyRect.X, dirtyRect.Right - BookmarkWidth);

        canvas.FillColor = AccentColor;
        canvas.FillPath(BrandGeometry.Bookmark(bookmarkX, dirtyRect.Y, BookmarkWidth));

        canvas.RestoreState();
    }
}
