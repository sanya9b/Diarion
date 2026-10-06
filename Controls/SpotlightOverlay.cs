using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// The dimmed layer of the home-screen guide: everything is covered except one rounded window around
/// the element being explained, which is drawn with a thin ring so the eye lands on it.
/// </summary>
/// <remarks>
/// The hole is a hole in the fill, not a clip. <c>ICanvas.SubtractFromClip</c> takes a rectangle and
/// only a rectangle, so cutting through the clip would give the spotlight square corners on a screen
/// where every card is rounded. Filling one path made of the full rect plus the rounded hole, with
/// even-odd winding, is the same idea and keeps the corners.
/// <para>
/// Draws only the rectangle it is handed. Working out where the target actually sits is the page's
/// job — this control never looks at the visual tree.
/// </para>
/// </remarks>
public class SpotlightOverlay : GraphicsView, IDrawable
{
    /// <summary>The target, in page coordinates. Empty means "no hole", i.e. a plain scrim.</summary>
    public static readonly BindableProperty TargetRectProperty =
        BindableProperty.Create(nameof(TargetRect), typeof(Rect), typeof(SpotlightOverlay), Rect.Zero, propertyChanged: OnVisualChanged);

    public Rect TargetRect
    {
        get => (Rect)GetValue(TargetRectProperty);
        set => SetValue(TargetRectProperty, value);
    }

    /// <summary>Colour of the dimmed area. Expected to carry its own alpha.</summary>
    public static readonly BindableProperty ScrimColorProperty =
        BindableProperty.Create(nameof(ScrimColor), typeof(Color), typeof(SpotlightOverlay), Color.FromArgb("#D0000000"), propertyChanged: OnVisualChanged);

    public Color ScrimColor
    {
        get => (Color)GetValue(ScrimColorProperty);
        set => SetValue(ScrimColorProperty, value);
    }

    /// <summary>Ring drawn around the window.</summary>
    public static readonly BindableProperty HighlightColorProperty =
        BindableProperty.Create(nameof(HighlightColor), typeof(Color), typeof(SpotlightOverlay), Colors.White, propertyChanged: OnVisualChanged);

    public Color HighlightColor
    {
        get => (Color)GetValue(HighlightColorProperty);
        set => SetValue(HighlightColorProperty, value);
    }

    /// <summary>How far the window is grown beyond the element, so the highlight does not clip it.</summary>
    public static readonly BindableProperty HolePaddingProperty =
        BindableProperty.Create(nameof(HolePadding), typeof(double), typeof(SpotlightOverlay), 8d, propertyChanged: OnVisualChanged);

    public double HolePadding
    {
        get => (double)GetValue(HolePaddingProperty);
        set => SetValue(HolePaddingProperty, value);
    }

    public static readonly BindableProperty HoleRadiusProperty =
        BindableProperty.Create(nameof(HoleRadius), typeof(double), typeof(SpotlightOverlay), 18d, propertyChanged: OnVisualChanged);

    public double HoleRadius
    {
        get => (double)GetValue(HoleRadiusProperty);
        set => SetValue(HoleRadiusProperty, value);
    }

    public SpotlightOverlay()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;

        // Same reason as the charts: a GraphicsView that gets its size after its first draw renders
        // blank and never asks again.
        Loaded += (_, _) => Invalidate();
        SizeChanged += (_, _) => Invalidate();
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
        => ((SpotlightOverlay)bindable).Invalidate();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var path = new PathF();
        path.AppendRectangle(dirtyRect);

        var hole = HoleRect(dirtyRect);
        if (hole.HasValue)
        {
            path.AppendRoundedRectangle(hole.Value, (float)HoleRadius);
        }

        canvas.FillColor = ScrimColor;
        canvas.FillPath(path, WindingMode.EvenOdd);

        if (hole.HasValue)
        {
            canvas.StrokeColor = HighlightColor;
            canvas.StrokeSize = 2f;
            canvas.DrawRoundedRectangle(hole.Value, (float)HoleRadius);
        }
    }

    /// <summary>The window to cut, padded and kept inside the canvas; <c>null</c> when there is nothing to point at.</summary>
    private RectF? HoleRect(RectF canvasRect)
    {
        var target = TargetRect;
        if (target.Width <= 0 || target.Height <= 0) return null;

        var padding = (float)HolePadding;
        var left = (float)target.X - padding;
        var top = (float)target.Y - padding;
        var right = (float)target.Right + padding;
        var bottom = (float)target.Bottom + padding;

        // Clamped rather than trusted: a target measured mid-scroll can land off the canvas, and a
        // hole hanging over the edge reads as a rendering fault rather than as a highlight.
        left = System.Math.Max(left, canvasRect.Left);
        top = System.Math.Max(top, canvasRect.Top);
        right = System.Math.Min(right, canvasRect.Right);
        bottom = System.Math.Min(bottom, canvasRect.Bottom);

        if (right - left <= 0 || bottom - top <= 0) return null;

        return new RectF(left, top, right - left, bottom - top);
    }
}
