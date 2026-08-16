using System.Linq;
using Foundation;
using UIKit;

namespace Diarion.Services;

/// <summary>
/// The iOS side of <see cref="IKeyboardInsetService"/>, and the one platform where the number is
/// ever anything but zero.
/// </summary>
/// <remarks>
/// iOS slides the keyboard over the window and leaves the layout alone, so anything docked to the
/// bottom is simply behind it. The keyboard announces where it is going before it goes, and the
/// distance from the top of that frame to the bottom of the window is exactly what has to be given
/// back — height on its own would be wrong, since a keyboard on its way out is full height too.
/// </remarks>
public sealed class IosKeyboardInsetService : IKeyboardInsetService, IDisposable
{
    private readonly NSObject _willChange;
    private readonly NSObject _willHide;

    public IosKeyboardInsetService()
    {
        _willChange = UIKeyboard.Notifications.ObserveWillChangeFrame(OnKeyboardMoving);
        _willHide = UIKeyboard.Notifications.ObserveWillHide(OnKeyboardLeaving);
    }

    public double Overlap { get; private set; }

    public event EventHandler? OverlapChanged;

    public void Dispose()
    {
        _willChange.Dispose();
        _willHide.Dispose();
    }

    private void OnKeyboardMoving(object? sender, UIKeyboardEventArgs e)
    {
        var frame = e.FrameEnd;

        // Falling back to the keyboard's own height keeps this useful if the window cannot be found:
        // the two agree whenever the window covers the screen, which is every case but Slide Over.
        var bottom = (double?)Window()?.Frame.Bottom ?? (double)frame.Bottom;

        Set(Math.Max(0, bottom - (double)frame.Top));
    }

    private void OnKeyboardLeaving(object? sender, UIKeyboardEventArgs e) => Set(0);

    private void Set(double overlap)
    {
        if (Math.Abs(overlap - Overlap) < 0.5) return;

        Overlap = overlap;
        OverlapChanged?.Invoke(this, EventArgs.Empty);
    }

    // Asked through MAUI rather than UIApplication: every direct route to the key window is
    // deprecated, and MAUI is holding the answer anyway.
    private static UIWindow? Window()
        => Application.Current?.Windows
            .Select(window => window.Handler?.PlatformView as UIWindow)
            .FirstOrDefault(window => window != null);
}
