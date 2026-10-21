using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Helpers;

/// <summary>
/// Where a view is on its page, for things that must line up across unrelated branches of the visual
/// tree — a popover in a page-level layer pointing at a control deep inside a scrolling card.
/// </summary>
public static class VisualTreeGeometry
{
    /// <summary>
    /// The view's bounds in the coordinate space of its <see cref="Page"/>, as drawn right now. Walks up
    /// the tree summing each element's offset and its translation — a container slid by an animation is
    /// drawn where it is translated to, not where it was laid out. A <see cref="ScrollView"/> on the way
    /// reports its content at a fixed position while the platform scrolls it, so its scroll offset is
    /// taken off once per scroll view. Scale is left out: press feedback shrinks a button by three
    /// percent for a moment, and chasing that would make a bubble twitch. Returns the view's own bounds
    /// if no page is found, which is as good as the caller can do.
    /// </summary>
    public static Rect BoundsOnPage(VisualElement view)
    {
        double x = view.X + view.TranslationX;
        double y = view.Y + view.TranslationY;
        var parent = view.Parent;

        while (parent is VisualElement element && parent is not Page)
        {
            x += element.X + element.TranslationX;
            y += element.Y + element.TranslationY;

            if (element is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }

            parent = element.Parent;
        }

        return new Rect(x, y, view.Width, view.Height);
    }

    /// <summary>The nearest <see cref="ScrollView"/> above the view, or null.</summary>
    public static ScrollView? FindScrollView(Element view)
    {
        var parent = view.Parent;
        while (parent != null)
        {
            if (parent is ScrollView scroll) return scroll;
            parent = parent.Parent;
        }

        return null;
    }
}
