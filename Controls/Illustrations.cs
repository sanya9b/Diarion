using System;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>Which empty screen an <see cref="Illustration"/> stands for.</summary>
public enum EmptyScene
{
    Notes,
    Habits,
    Prompts,
    Cycle,
    Mood,
    Sleep,
    Productivity,
    Search
}

/// <summary>
/// One line-art scene in a 96×96 space: open strokes, a few filled dots, and where the bookmark hangs.
/// Strokes are SVG path markup so a scene reads as a drawing, not as a list of calls.
/// </summary>
public sealed record IllustrationScene(string[] Strokes, PointF[] Dots, PointF BookmarkAt, float BookmarkWidth = 10f);

/// <summary>
/// The catalogue of empty-state scenes (spec 16, R3.2). Rules, in order of importance: no faces, no
/// people, no animals; three to six strokes that read in three seconds; one Coral element per scene,
/// and that element is always the bookmark. Everything else takes the muted ink colour, so a scene
/// looks the same in both themes with only the ink swapped.
/// </summary>
public static class Illustrations
{
    public const float CanvasSize = 96f;

    public static IllustrationScene For(EmptyScene scene) => scene switch
    {
        // A page with three lines of text; the bookmark hangs over its top-right edge.
        EmptyScene.Notes => new IllustrationScene(
            new[] { Card(26, 18, 40, 60, 4), "M 34 34 L 58 34", "M 34 44 L 54 44", "M 34 54 L 50 54" },
            Array.Empty<PointF>(),
            new PointF(54, 14)),

        // Three days in a row, the first one ticked.
        EmptyScene.Habits => new IllustrationScene(
            new[] { Box(22, 38, 14), Box(41, 38, 14), Box(60, 38, 14), "M 25 45 L 28 48 L 33 41" },
            Array.Empty<PointF>(),
            new PointF(68, 30)),

        // A speech bubble with a question mark — the diary asking, nobody's face.
        EmptyScene.Prompts => new IllustrationScene(
            new[]
            {
                "M 28 24 L 68 24 Q 72 24 72 28 L 72 56 Q 72 60 68 60 L 42 60 L 34 68 L 34 60 L 28 60 Q 24 60 24 56 L 24 28 Q 24 24 28 24 Z",
                "M 43 38 Q 43 31 48 31 Q 53 31 53 37 Q 53 41 48 43 L 48 47"
            },
            new[] { new PointF(48, 52) },
            new PointF(62, 20)),

        // A ring with a mark at the top: a cycle with its first day not yet placed.
        EmptyScene.Cycle => new IllustrationScene(
            new[] { Circle(48, 48, 24), "M 48 24 L 48 31" },
            Array.Empty<PointF>(),
            new PointF(64, 22)),

        // A baseline and one gentle wave — mood over a week.
        EmptyScene.Mood => new IllustrationScene(
            new[] { "M 20 62 L 76 62", "M 20 54 C 30 36 38 36 48 52 C 58 68 66 68 76 50" },
            Array.Empty<PointF>(),
            new PointF(66, 26)),

        // A crescent and two small stars.
        EmptyScene.Sleep => new IllustrationScene(
            new[]
            {
                "M 56 22 A 26 26 0 1 0 70 62 A 20 20 0 1 1 56 22 Z",
                "M 78 38 L 78 44 M 75 41 L 81 41",
                "M 28 72 L 28 76 M 26 74 L 30 74"
            },
            Array.Empty<PointF>(),
            new PointF(58, 16)),

        // A checklist card: one row done, one waiting.
        EmptyScene.Productivity => new IllustrationScene(
            new[]
            {
                Card(22, 22, 52, 52, 4),
                Box(30, 34, 10), "M 32 39 L 35 42 L 39 36", "M 46 39 L 66 39",
                Box(30, 52, 10), "M 46 57 L 62 57"
            },
            Array.Empty<PointF>(),
            new PointF(62, 18)),

        // A magnifier.
        EmptyScene.Search => new IllustrationScene(
            new[] { Circle(44, 44, 17), "M 56 56 L 72 72" },
            Array.Empty<PointF>(),
            new PointF(54, 20)),

        _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, null)
    };

    // Invariant formatting on purpose: a Ukrainian thread culture writes 4,5 and the path parser reads
    // that as two numbers.
    private static string Card(float x, float y, float w, float h, float r) =>
        FormattableString.Invariant($"M {x + r} {y} L {x + w - r} {y} Q {x + w} {y} {x + w} {y + r} L {x + w} {y + h - r} Q {x + w} {y + h} {x + w - r} {y + h} ") +
        FormattableString.Invariant($"L {x + r} {y + h} Q {x} {y + h} {x} {y + h - r} L {x} {y + r} Q {x} {y} {x + r} {y} Z");

    private static string Box(float x, float y, float s) => FormattableString.Invariant(
        $"M {x} {y} L {x + s} {y} L {x + s} {y + s} L {x} {y + s} Z");

    private static string Circle(float cx, float cy, float r) => FormattableString.Invariant(
        $"M {cx - r} {cy} A {r} {r} 0 1 1 {cx + r} {cy} A {r} {r} 0 1 1 {cx - r} {cy}");
}
