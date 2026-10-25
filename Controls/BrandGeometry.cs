using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// The app mark as geometry, taken verbatim from <c>Resources/Images/app_logo.svg</c> so the letter
/// on the onboarding screen is the same letter as on the icon. Everything is in the icon's 280×280
/// space; callers scale the canvas. The bookmark is built rather than parsed so the empty-state
/// scenes can place a smaller copy of it anywhere without a second copy of the coordinates.
/// </summary>
public static class BrandGeometry
{
    public const float CanvasSize = 280f;

    /// <summary>Outer and inner contour of the D. Fill with even-odd so the counter stays open.</summary>
    public const string LetterD =
        "M 78 70 L 78 218 L 148 218 C 215 218 222 175 222 144 C 222 113 215 70 148 70 Z " +
        "M 112 102 L 112 186 L 148 186 C 184 186 190 165 190 144 C 190 123 184 102 148 102 Z";

    /// <summary>Where the bookmark hangs on the icon: left edge, top, width. Height follows the ratio.</summary>
    public const float BookmarkX = 168f;
    public const float BookmarkY = 58f;
    public const float BookmarkWidth = 44f;

    /// <summary>The thin line under the letter.</summary>
    public const float RuleX1 = 62f;
    public const float RuleX2 = 218f;
    public const float RuleY = 240f;

    private const float BookmarkHeightRatio = 80f / 44f;
    private const float BookmarkNotchRatio = 16f / 80f;

    private static PathF? _letter;

    /// <summary>Parsed once; a <see cref="PathF"/> is only read while drawing, so sharing it is safe.</summary>
    public static PathF Letter => _letter ??= PathBuilder.Build(LetterD);

    /// <summary>A bookmark of the icon's proportions with its top-left corner at (x, y).</summary>
    public static PathF Bookmark(float x, float y, float width)
    {
        var height = width * BookmarkHeightRatio;
        var notch = height * BookmarkNotchRatio;

        var path = new PathF();
        path.MoveTo(x, y);
        path.LineTo(x, y + height);
        path.LineTo(x + width / 2f, y + height - notch);
        path.LineTo(x + width, y + height);
        path.LineTo(x + width, y);
        path.Close();
        return path;
    }
}
