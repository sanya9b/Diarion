using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Helpers;

/// <summary>
/// Where a view is on screen, for things that must line up across unrelated branches of the visual
/// tree — a popover in a page-level layer pointing at a control deep inside a scrolling card.
/// </summary>
public static class VisualTreeGeometry
{
    /// <summary>
    /// The view's bounds, as drawn right now, in one space shared by every view in the window. Only
    /// differences between two results mean anything: callers subtract the bounds of the layer they draw
    /// in. The position is asked of the platform, which knows where the view really is after scrolling,
    /// translations and system insets. Summing X, Y and ScrollY up the tree, as this did before, went wrong
    /// on iPhone once the diary scroll reached the top of the page: the reported scroll offset could lag
    /// the drawn one, and the bubble sat a face's height below its face (owner, TestFlight build 10).
    /// The size is the view's own and the platform's centre is kept, so the three-percent press feedback
    /// on a face does not make a bubble twitch. Falls back to the tree walk when the view has no platform
    /// view yet.
    /// </summary>
    public static Rect BoundsOnPage(VisualElement view)
    {
        if (PlatformCentre(view) is Point centre && view.Width > 0 && view.Height > 0)
            return new Rect(centre.X - view.Width / 2, centre.Y - view.Height / 2, view.Width, view.Height);

        // Not on screen as itself yet (hidden, or not laid out): walk up to the nearest ancestor the
        // platform can place and add the laid-out offsets from there. Never mix the platform's space
        // with the tree's own: two results from different spaces differ by the page's offset in the
        // window, and a bubble placed from that difference lands off its button.
        return TreeBounds(view);
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

    /// <summary>The centre of the view as drawn, in device-independent units relative to its window.</summary>
    private static Point? PlatformCentre(VisualElement view)
    {
        var platformView = view.Handler?.PlatformView;
        if (platformView is null) return null;

#if IOS || MACCATALYST
        if (platformView is UIKit.UIView ui && ui.Window is not null)
        {
            var r = ui.ConvertRectToView(ui.Bounds, null);
            return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        }
#elif ANDROID
        if (platformView is Android.Views.View av && av.IsAttachedToWindow && av.Context?.Resources?.DisplayMetrics is { } metrics)
        {
            var location = new int[2];
            av.GetLocationInWindow(location);
            var density = metrics.Density;
            // GetLocationInWindow applies the view's own scale; the centre is where it stays put.
            return new Point((location[0] + av.Width * av.ScaleX / 2) / density,
                             (location[1] + av.Height * av.ScaleY / 2) / density);
        }
#elif WINDOWS
        if (platformView is Microsoft.UI.Xaml.FrameworkElement fe && fe.XamlRoot is not null)
        {
            var origin = fe.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            var far = fe.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(fe.ActualWidth, fe.ActualHeight));
            return new Point((origin.X + far.X) / 2, (origin.Y + far.Y) / 2);
        }
#endif
        return null;
    }

    /// <summary>
    /// For a view not yet on screen: walk up the tree summing each element's offset and translation,
    /// until an ancestor the platform can place takes over; a scroll view's offset comes off on the way.
    /// </summary>
    private static Rect TreeBounds(VisualElement view)
    {
        double x = view.X + view.TranslationX;
        double y = view.Y + view.TranslationY;
        var parent = view.Parent;

        while (parent is VisualElement element && parent is not Page)
        {
            // An ancestor the platform can place anchors the rest in the platform's space.
            if (PlatformCentre(element) is Point c && element.Width > 0 && element.Height > 0)
            {
                var left = c.X - element.Width / 2;
                var top = c.Y - element.Height / 2;
                if (element is ScrollView sv)
                {
                    left -= sv.ScrollX;
                    top -= sv.ScrollY;
                }
                return new Rect(left + x, top + y, view.Width, view.Height);
            }

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
}
