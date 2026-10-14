using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// Draws one <see cref="EmptyScene"/> from <see cref="Illustrations"/>: muted line art plus the Coral
/// bookmark. Colours arrive through <c>DynamicResource</c> like the charts', so the scene repaints on a
/// theme switch instead of asking the OS which theme is on.
/// </summary>
public class Illustration : GraphicsView, IDrawable
{
    public static readonly BindableProperty SceneProperty =
        BindableProperty.Create(nameof(Scene), typeof(EmptyScene), typeof(Illustration), EmptyScene.Notes, propertyChanged: OnVisualChanged);

    public EmptyScene Scene
    {
        get => (EmptyScene)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>Ink for the strokes and dots — the muted text colour of the theme.</summary>
    public static readonly BindableProperty StrokeColorProperty =
        BindableProperty.Create(nameof(StrokeColor), typeof(Color), typeof(Illustration), Color.FromArgb("#929FA7"), propertyChanged: OnVisualChanged);

    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    /// <summary>The bookmark. The only accent a scene is allowed.</summary>
    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(Illustration), Color.FromArgb("#C26D53"), propertyChanged: OnVisualChanged);

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    private const float StrokeWidth = 1.5f;
    private const float DotRadius = 1.4f;

    // Parsed strokes per scene, built on first use. Paths are only read while drawing.
    private static readonly Dictionary<EmptyScene, PathF[]> StrokeCache = new();

    public Illustration()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        Loaded += (_, _) => Invalidate();
        SizeChanged += (_, _) => Invalidate();
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
        => ((Illustration)bindable).Invalidate();

    private static PathF[] StrokesFor(EmptyScene scene)
    {
        lock (StrokeCache)
        {
            if (!StrokeCache.TryGetValue(scene, out var paths))
            {
                var markup = Illustrations.For(scene).Strokes;
                paths = new PathF[markup.Length];
                for (var i = 0; i < markup.Length; i++)
                    paths[i] = PathBuilder.Build(markup[i]);
                StrokeCache[scene] = paths;
            }

            return paths;
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var size = Math.Min(dirtyRect.Width, dirtyRect.Height);
        if (size <= 0)
            return;

        var scene = Illustrations.For(Scene);
        var scale = size / Illustrations.CanvasSize;

        canvas.SaveState();
        canvas.Antialias = true;
        canvas.Translate(dirtyRect.X + (dirtyRect.Width - size) / 2f, dirtyRect.Y + (dirtyRect.Height - size) / 2f);
        canvas.Scale(scale, scale);

        canvas.StrokeColor = StrokeColor;
        canvas.StrokeSize = StrokeWidth;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        foreach (var path in StrokesFor(Scene))
            canvas.DrawPath(path);

        canvas.FillColor = StrokeColor;
        foreach (var dot in scene.Dots)
            canvas.FillCircle(dot, DotRadius);

        canvas.FillColor = AccentColor;
        canvas.FillPath(BrandGeometry.Bookmark(scene.BookmarkAt.X, scene.BookmarkAt.Y, scene.BookmarkWidth));

        canvas.RestoreState();
    }
}
