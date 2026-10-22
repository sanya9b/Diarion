using Diarion.Helpers;
using Diarion.Services;

namespace Diarion.Resources.Styles;

/// <summary>
/// Motion tokens: the only durations and curves UI code animates with. Values follow the project's
/// motion rules (see .github/instructions/ui-motion.instructions.md): UI motion stays under ~300 ms,
/// entrances and exits ease out, and nothing on screen eases in.
/// </summary>
public static class Motion
{
    /// <summary>Press feedback, down and back up.</summary>
    public const int PressMs = 120;

    /// <summary>Toggles, chips, cross-fades of content the user switches many times a day.</summary>
    public const int FastMs = 150;

    /// <summary>Sections expanding, content entering, anything dropdown-sized.</summary>
    public const int StandardMs = 240;

    /// <summary>Sheets, overlays and dialogs — the ceiling for in-app UI motion.</summary>
    public const int OverlayMs = 320;

    /// <summary>
    /// One breath of a living picture — rain falling, a flower swaying (the mood faces' drawn sets).
    /// Not a UI transition: an illustration's rhythm, played only on the chosen face.
    /// </summary>
    public const int GlyphLoopMs = 2400;

    /// <summary>How many breaths the chosen face plays before it rests: delight on the choice, then quiet.</summary>
    public const int GlyphLoops = 3;

    /// <summary>Entering, exiting, and the default when unsure: cubic-bezier(0.23, 1, 0.32, 1).</summary>
    public static readonly Easing EaseOut = new(t => CubicBezier.EaseOut.Solve(t));

    /// <summary>Movement that stays on screen (reorder, morph): cubic-bezier(0.77, 0, 0.175, 1).</summary>
    public static readonly Easing EaseInOut = new(t => CubicBezier.EaseInOut.Solve(t));

    /// <summary>Bottom sheets and drawers: cubic-bezier(0.32, 0.72, 0, 1).</summary>
    public static readonly Easing Sheet = new(t => CubicBezier.Sheet.Solve(t));

    private static IMotionSettings? _settings;

    /// <summary>Wire the platform preference once at startup; until then motion is assumed allowed.</summary>
    public static void Configure(IMotionSettings settings) => _settings = settings;

    /// <summary>The user asked the OS to minimise animation. Movement goes, fades stay.</summary>
    public static bool ReduceMotion => _settings?.ReduceMotion ?? false;

    /// <summary>A translation distance that collapses to zero under reduce motion.</summary>
    public static double Offset(double value) => ReduceMotion ? 0 : value;

    /// <summary>A starting scale that collapses to 1 under reduce motion.</summary>
    public static double Scale(double value) => ReduceMotion ? 1 : value;
}
