using System;
using Diarion.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// Draws one mood face of a drawn <see cref="MoodIconSet"/> — weather or a plant in a pot (spec 18).
/// The art lives on a 64-unit square, as the approved drawings did, and is fitted into the view.
/// <see cref="Time"/> moves it: each whole number is one breath (<c>Motion.GlyphLoopMs</c>), null is
/// the face at rest. <see cref="MoodGlyph"/> decides when it moves.
/// </summary>
public class MoodArt : GraphicsView, IDrawable
{
    private EmotionQuadrant _quadrant;
    private MoodIconSet _set;
    private double? _time;

    public MoodArt()
    {
        Drawable = this;
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
    }

    public EmotionQuadrant Quadrant
    {
        get => _quadrant;
        set { _quadrant = value; Invalidate(); }
    }

    public MoodIconSet Set
    {
        get => _set;
        set { _set = value; Invalidate(); }
    }

    public double? Time
    {
        get => _time;
        set { _time = value; Invalidate(); }
    }

    // The part of the 64-unit square each set fills.
    private static readonly RectF WeatherFrame = new(5, 5, 54, 54);
    private static readonly RectF PlantFrame = new(6, 4, 52, 56);

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (_set == MoodIconSet.Emoji || _quadrant == EmotionQuadrant.None) return;
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;

        var frame = _set == MoodIconSet.Weather ? WeatherFrame : PlantFrame;
        var scale = Math.Min(dirtyRect.Width / frame.Width, dirtyRect.Height / frame.Height);

        canvas.SaveState();
        canvas.Antialias = true;
        canvas.Translate(dirtyRect.Center.X, dirtyRect.Center.Y);
        canvas.Scale(scale, scale);
        canvas.Translate(-frame.Center.X, -frame.Center.Y);

        // Where in the current breath, 0..1; null at rest, where everything sits as drawn.
        float? p = _time is { } t ? (float)(t - Math.Floor(t)) : null;

        if (_set == MoodIconSet.Weather) DrawWeather(canvas, _quadrant, p);
        else DrawPlant(canvas, _quadrant, p);

        canvas.RestoreState();
    }

    // ------------------------------------------------------------------ weather

    private static readonly PathF RainCloud = PathBuilder.Build("M 17 38 H 47 A 8.5 8.5 0 0 0 46 21 A 13 13 0 0 0 21.5 22 A 8 8 0 0 0 17 38 Z");
    private static readonly PathF StormCloud = PathBuilder.Build("M 17 36 H 47 A 8.5 8.5 0 0 0 46 19 A 13 13 0 0 0 21.5 20 A 8 8 0 0 0 17 36 Z");
    private static readonly PathF PlainCloud = PathBuilder.Build("M 17 44 H 47 A 8.5 8.5 0 0 0 46 27 A 13 13 0 0 0 21.5 28 A 8 8 0 0 0 17 44 Z");
    private static readonly PathF Drop = PathBuilder.Build("M 0 0 Q 2.2 3.6 1.2 5.2 Q 0 6.6 -1.2 5.2 Q -2.2 3.6 0 0 Z");
    private static readonly PathF Bolt = PathBuilder.Build("M 34 34 L 26 47 H 32 L 28.5 58 L 41 43.5 H 34.5 L 38 34 Z");
    private static readonly PathF Moon = PathBuilder.Build("M 36 14.5 A 18 18 0 1 0 49.5 42 A 14 14 0 1 1 36 14.5 Z");
    private static readonly PathF Sparkle = PathBuilder.Build("M 0 -1 L 0.29 -0.29 L 1 0 L 0.29 0.29 L 0 1 L -0.29 0.29 L -1 0 L -0.29 -0.29 Z");

    private static readonly (float X, float Y)[] RainDrops = { (23, 44), (32, 46), (41, 44) };

    private static void DrawWeather(ICanvas canvas, EmotionQuadrant quadrant, float? p)
    {
        // Clouds drift a little side to side.
        var drift = p is { } d ? 1.5f * MathF.Sin(2 * MathF.PI * d) : 0f;

        switch (quadrant)
        {
            case EmotionQuadrant.UnpleasantCalm:
                // A light rim keeps the grey cloud apart from the grey disc it sits on when chosen.
                Drifted(canvas, drift, () =>
                {
                    FillLinear(canvas, RainCloud, "#DCE4EB", "#9AA9B7");
                    Stroke(canvas, RainCloud, "#F4F7FA", 1.2f);
                });
                for (var i = 0; i < RainDrops.Length; i++)
                {
                    // Two falls per breath, the drops a third apart; each fades in and out of sight.
                    var (x, y) = RainDrops[i];
                    float fall = 0, alpha = 1;
                    if (p is { } r)
                    {
                        var q = Frac(r * 2 + i / 3f);
                        fall = -3 + 9 * q;
                        alpha = q < 0.25f ? q / 0.25f : 1 - (q - 0.25f) / 0.75f;
                    }
                    canvas.SaveState();
                    canvas.Alpha = alpha;
                    canvas.Translate(x, y + fall);
                    canvas.Scale(1.3f, 1.3f);
                    FillLinear(canvas, Drop, "#B5E2FA", "#3E8FD0");
                    canvas.RestoreState();
                }
                break;

            case EmotionQuadrant.UnpleasantEnergetic:
                Drifted(canvas, drift, () =>
                {
                    FillLinear(canvas, StormCloud, "#C9CFD5", "#848E98");
                    Stroke(canvas, StormCloud, "#F4F7FA", 1.2f);
                });
                canvas.SaveState();
                // The bolt flickers twice near the end of each breath.
                if (p is { } f && ((f > 0.70f && f < 0.76f) || (f > 0.80f && f < 0.84f))) canvas.Alpha = 0.3f;
                FillLinear(canvas, Bolt, "#FFD978", "#F09A3A");
                Stroke(canvas, Bolt, "#B8661F", 1f);
                canvas.RestoreState();
                break;

            case EmotionQuadrant.Neutral:
                Drifted(canvas, drift, () =>
                {
                    FillLinear(canvas, PlainCloud, "#FBFBFA", "#CDD3D8");
                    Stroke(canvas, PlainCloud, "#9FA9B2", 1.4f);
                });
                break;

            case EmotionQuadrant.PleasantCalm:
                canvas.SaveState();
                canvas.Alpha = 0.14f;
                canvas.FillColor = Color.FromArgb("#F4D58A");
                canvas.FillCircle(30, 34, 20);
                canvas.RestoreState();
                FillLinear(canvas, Moon, "#FFF4CF", "#EFC968", diagonal: true);
                canvas.SaveState();
                canvas.Alpha = 0.45f;
                canvas.FillColor = Color.FromArgb("#E3B95A");
                canvas.FillCircle(25, 38, 2.2f);
                canvas.FillCircle(31, 46, 1.5f);
                canvas.RestoreState();
                Twinkle(canvas, 50, 14.5f, 4.5f, p, 0);
                Twinkle(canvas, 54, 27.8f, 2.8f, p, 0.5f);
                break;

            case EmotionQuadrant.PleasantEnergetic:
                canvas.SaveState();
                // An eighth of a turn per breath: the rays line up again where they started.
                if (p is { } s) canvas.Rotate(45 * s, 32, 32);
                canvas.StrokeColor = Color.FromArgb("#F3A64A");
                canvas.StrokeSize = 4.2f;
                canvas.StrokeLineCap = LineCap.Round;
                for (var i = 0; i < 8; i++)
                {
                    var a = i * MathF.PI / 4;
                    canvas.DrawLine(32 + 17.5f * MathF.Cos(a), 32 + 17.5f * MathF.Sin(a), 32 + 24.5f * MathF.Cos(a), 32 + 24.5f * MathF.Sin(a));
                }
                canvas.RestoreState();
                FillRadialCircle(canvas, 32, 32, 12.5f, "#FFEFA8", "#FBC85A", "#F0A23E");
                Shine(canvas, 27.5f, 27, 4, 2.4f, -30);
                break;
        }
    }

    private static void Twinkle(ICanvas canvas, float x, float y, float radius, float? p, float offset)
    {
        var shrink = p is { } t ? 0.5f * Wave(t + offset) : 0f;
        canvas.SaveState();
        canvas.Alpha = 1 - shrink;
        canvas.Translate(x, y);
        canvas.Scale(radius * (1 - shrink), radius * (1 - shrink));
        canvas.FillColor = Color.FromArgb("#F4D58A");
        canvas.FillPath(Sparkle);
        canvas.RestoreState();
    }

    // ------------------------------------------------------------------- plants

    private static readonly PathF Pot = PathBuilder.Build("M 22 46.5 H 42 L 39.5 58.5 H 24.5 Z");
    private static readonly PathF Wind = PathBuilder.Build("M 8 16 H 19 M 6 25 H 15 M 10 34 H 18");
    private static readonly PathF Tulip = PathBuilder.Build("M 24.5 11 Q 24 24 32 25.5 Q 40 24 39.5 11 L 35.8 15 L 32 9 L 28.2 15 Z");

    // Stems start inside the pot, under its rim, so a plant that lifts never shows a gap.
    private static readonly PathF SadStem = PathBuilder.Build("M 32 47 Q 31 29 38 25 Q 44.5 22 45.5 28.5");
    private static readonly PathF SadLeaf = PathBuilder.Build("M 31.6 37 Q 23.5 36.5 21 43 Q 27.5 43 31.6 37 Z");
    private static readonly PathF WindStem = PathBuilder.Build("M 32 47 Q 32 31 39.5 23");
    private static readonly PathF WindLeaf = PathBuilder.Build("M 32.4 35.5 Q 39.5 30 44 33 Q 38.5 38.5 32.4 35.5 Z");
    private static readonly PathF SproutStem = PathBuilder.Build("M 32 47 V 30");
    private static readonly PathF SproutLeft = PathBuilder.Build("M 32 31 Q 24 21.5 17.5 27 Q 23.5 34.5 32 31 Z");
    private static readonly PathF SproutRight = PathBuilder.Build("M 32 31 Q 40 21.5 46.5 27 Q 40.5 34.5 32 31 Z");
    private static readonly PathF TulipStem = PathBuilder.Build("M 32 47 V 24");
    private static readonly PathF TulipLeft = PathBuilder.Build("M 32 37 Q 21.5 35.5 19 40.5 Q 27 42.5 32 37 Z");
    private static readonly PathF TulipRight = PathBuilder.Build("M 32 32.5 Q 42.5 31 45 36 Q 37 38 32 32.5 Z");
    private static readonly PathF BloomStem = PathBuilder.Build("M 32 47 V 25");
    private static readonly PathF BloomLeft = PathBuilder.Build("M 32 37 Q 21.5 32.5 19 38 Q 26.5 42 32 37 Z");
    private static readonly PathF BloomRight = PathBuilder.Build("M 32 31.5 Q 42.5 27 45 32.5 Q 37.5 36.5 32 31.5 Z");

    private const string StemColor = "#6F8F5E";
    private const string Leaf = "#8FAE7B";
    private const string LeafLight = "#9DBB88";

    private static void DrawPlant(ICanvas canvas, EmotionQuadrant quadrant, float? p)
    {
        switch (quadrant)
        {
            case EmotionQuadrant.UnpleasantCalm:
            {
                // Wilted: the whole plant sags a little with each breath.
                var sag = p is { } t ? Wave(t) : 0f;
                canvas.SaveState();
                ScaleAbout(canvas, 32, 43, 1 + 0.02f * sag, 1 - 0.03f * sag);
                Stem(canvas, SadStem);
                Fill(canvas, SadLeaf, "#9DB08F");
                Petals(canvas, 45.5f, 33.5f, 7, 4.8f, 2.6f, 4.4f, "#C9D5E0", "#93A7BA");
                canvas.FillColor = Color.FromArgb("#7E93A8");
                canvas.FillCircle(45.5f, 33.5f, 3.2f);
                canvas.RestoreState();
                DrawPot(canvas);
                Petal(canvas, 25.5f, 42.2f, 1.8f, 3.4f, -70, "#C9D5E0", "#93A7BA");
                break;
            }

            case EmotionQuadrant.UnpleasantEnergetic:
            {
                // In the wind: a quick shiver late in each breath, and two petals carried off.
                var shiver = 0f;
                if (p is { } t && t > 0.6f && t < 0.75f)
                    shiver = 0.8f * MathF.Sin(2 * MathF.PI * (t - 0.6f) * 40) * (1 - (t - 0.6f) / 0.15f);
                canvas.SaveState();
                canvas.Translate(shiver, 0);
                Stem(canvas, WindStem);
                Fill(canvas, WindLeaf, Leaf);
                Petals(canvas, 41, 19, 7, 6.4f, 3.3f, 5.4f, "#FFC9A0", "#EE8F5E", skipA: 1, skipB: 2);
                canvas.FillColor = Color.FromArgb("#B9654B");
                canvas.FillCircle(41, 19, 4);
                Stroke(canvas, Wind, "#AEB9C2", 2.4f);
                canvas.RestoreState();
                Flying(canvas, 52.5f, 12, 2.5f, 4, 50, p, 0);
                Flying(canvas, 55, 23, 2.1f, 3.4f, 75, p, 0.37f);
                DrawPot(canvas);
                break;
            }

            case EmotionQuadrant.Neutral:
            {
                var drift = p is { } t ? 1.5f * MathF.Sin(2 * MathF.PI * t) : 0f;
                canvas.SaveState();
                canvas.Translate(drift, 0);
                Stem(canvas, SproutStem);
                Fill(canvas, SproutLeft, Leaf);
                Fill(canvas, SproutRight, LeafLight);
                canvas.RestoreState();
                DrawPot(canvas);
                break;
            }

            case EmotionQuadrant.PleasantCalm:
            {
                // A slow breath: the tulip rises a touch and settles.
                var breath = p is { } t ? Wave(t) : 0f;
                canvas.SaveState();
                ScaleAbout(canvas, 32, 43, 1 + 0.02f * breath, 1 + 0.045f * breath);
                Stem(canvas, TulipStem);
                Fill(canvas, TulipLeft, Leaf);
                Fill(canvas, TulipRight, LeafLight);
                FillLinear(canvas, Tulip, "#FAD3C8", "#E9A08E");
                Stroke(canvas, Tulip, "#D98A78", 0.8f);
                canvas.RestoreState();
                DrawPot(canvas);
                break;
            }

            case EmotionQuadrant.PleasantEnergetic:
            {
                // In bloom: a small hop early in each breath while the flower turns.
                var hop = p is { } t && t < 0.4f ? -3 * MathF.Sin(MathF.PI * t / 0.4f) : 0f;
                canvas.SaveState();
                canvas.Translate(0, hop);
                Stem(canvas, BloomStem);
                Fill(canvas, BloomLeft, Leaf);
                Fill(canvas, BloomRight, LeafLight);
                canvas.SaveState();
                // A ninth of a turn per breath: nine petals line up again where they started.
                if (p is { } s) canvas.Rotate(40 * s, 32, 16);
                Petals(canvas, 32, 16, 9, 7.6f, 3.8f, 6.2f, "#FFE29A", "#F3B04F");
                canvas.RestoreState();
                canvas.FillColor = Color.FromArgb("#B9654B");
                canvas.FillCircle(32, 16, 5.6f);
                canvas.SaveState();
                canvas.Alpha = 0.35f;
                canvas.FillColor = Colors.White;
                canvas.FillCircle(30.3f, 14.3f, 1.6f);
                canvas.RestoreState();
                canvas.RestoreState();
                DrawPot(canvas);
                break;
            }
        }
    }

    private static void DrawPot(ICanvas canvas)
    {
        FillLinear(canvas, Pot, "#DB8C6E", "#B4614A", diagonal: true);
        Stroke(canvas, Pot, "#9E4F39", 1.2f);
        canvas.FillColor = Color.FromArgb("#D98466");
        canvas.FillRoundedRectangle(20, 43, 24, 5.5f, 2.2f);
        canvas.StrokeColor = Color.FromArgb("#9E4F39");
        canvas.StrokeSize = 1.2f;
        canvas.DrawRoundedRectangle(20, 43, 24, 5.5f, 2.2f);
    }

    private static void Stem(ICanvas canvas, PathF stem)
    {
        canvas.StrokeColor = Color.FromArgb(StemColor);
        canvas.StrokeSize = 3;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(stem);
    }

    /// <summary>A ring of petals round (cx, cy); the two skipped ones are gone with the wind.</summary>
    private static void Petals(ICanvas canvas, float cx, float cy, int count, float distance, float rx, float ry,
        string top, string bottom, int skipA = -1, int skipB = -1)
    {
        for (var i = 0; i < count; i++)
        {
            if (i == skipA || i == skipB) continue;
            canvas.SaveState();
            canvas.Rotate(i * 360f / count, cx, cy);
            FillLinearEllipse(canvas, cx - rx, cy - distance - ry, 2 * rx, 2 * ry, top, bottom);
            canvas.RestoreState();
        }
    }

    private static void Petal(ICanvas canvas, float cx, float cy, float rx, float ry, float degrees, string top, string bottom)
    {
        canvas.SaveState();
        canvas.Rotate(degrees, cx, cy);
        FillLinearEllipse(canvas, cx - rx, cy - ry, 2 * rx, 2 * ry, top, bottom);
        canvas.RestoreState();
    }

    /// <summary>A petal the wind took: it waits, then drifts up and away and fades.</summary>
    private static void Flying(ICanvas canvas, float cx, float cy, float rx, float ry, float degrees, float? p, float offset)
    {
        float dx = 0, dy = 0, alpha = 1;
        if (p is { } t)
        {
            var q = Frac(t + offset);
            var k = q < 0.4f ? 0 : Math.Min(1, (q - 0.4f) / 0.5f);
            dx = 4 * k;
            dy = -3 * k;
            alpha = 1 - k;
        }
        canvas.SaveState();
        canvas.Alpha = alpha;
        canvas.Translate(dx, dy);
        Petal(canvas, cx, cy, rx, ry, degrees, "#FFC9A0", "#EE8F5E");
        canvas.RestoreState();
    }

    // ------------------------------------------------------------------ helpers

    private static void Drifted(ICanvas canvas, float dx, Action draw)
    {
        canvas.SaveState();
        canvas.Translate(dx, 0);
        draw();
        canvas.RestoreState();
    }

    private static void ScaleAbout(ICanvas canvas, float x, float y, float sx, float sy)
    {
        canvas.Translate(x, y);
        canvas.Scale(sx, sy);
        canvas.Translate(-x, -y);
    }

    private static void Fill(ICanvas canvas, PathF path, string color)
    {
        canvas.FillColor = Color.FromArgb(color);
        canvas.FillPath(path);
    }

    private static void Stroke(ICanvas canvas, PathF path, string color, float width)
    {
        canvas.StrokeColor = Color.FromArgb(color);
        canvas.StrokeSize = width;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(path);
    }

    private static void FillLinear(ICanvas canvas, PathF path, string top, string bottom, bool diagonal = false)
    {
        canvas.SaveState();
        canvas.SetFillPaint(Linear(top, bottom, diagonal), path.Bounds);
        canvas.FillPath(path);
        canvas.RestoreState();
    }

    private static void FillLinearEllipse(ICanvas canvas, float x, float y, float width, float height, string top, string bottom)
    {
        canvas.SaveState();
        canvas.SetFillPaint(Linear(top, bottom, diagonal: false), new RectF(x, y, width, height));
        canvas.FillEllipse(x, y, width, height);
        canvas.RestoreState();
    }

    private static void FillRadialCircle(ICanvas canvas, float cx, float cy, float r, string inner, string middle, string outer)
    {
        var bounds = new RectF(cx - r, cy - r, 2 * r, 2 * r);
        canvas.SaveState();
        canvas.SetFillPaint(new RadialGradientPaint
        {
            Center = new Point(0.4, 0.35),
            Radius = 0.7,
            GradientStops = new[]
            {
                new PaintGradientStop(0, Color.FromArgb(inner)),
                new PaintGradientStop(0.6f, Color.FromArgb(middle)),
                new PaintGradientStop(1, Color.FromArgb(outer))
            }
        }, bounds);
        canvas.FillEllipse(bounds);
        canvas.RestoreState();
    }

    private static void Shine(ICanvas canvas, float cx, float cy, float rx, float ry, float degrees)
    {
        canvas.SaveState();
        canvas.Rotate(degrees, cx, cy);
        canvas.Alpha = 0.5f;
        canvas.FillColor = Colors.White;
        canvas.FillEllipse(cx - rx, cy - ry, 2 * rx, 2 * ry);
        canvas.RestoreState();
    }

    private static LinearGradientPaint Linear(string top, string bottom, bool diagonal) => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = diagonal ? new Point(1, 1) : new Point(0, 1),
        GradientStops = new[]
        {
            new PaintGradientStop(0, Color.FromArgb(top)),
            new PaintGradientStop(1, Color.FromArgb(bottom))
        }
    };

    private static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>0 at the start of a breath, 1 halfway, 0 again at its end — a smooth in-and-out.</summary>
    private static float Wave(float t) => 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * t);
}
