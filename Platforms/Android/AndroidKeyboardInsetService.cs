using Android.App;
using Android.Views;
using Microsoft.Maui.ApplicationModel;
using View = Android.Views.View;

namespace Diarion.Services;

/// <summary>
/// The Android side of <see cref="IKeyboardInsetService"/>: normally zero, because the window has
/// already moved.
/// </summary>
/// <remarks>
/// Android resizes the window when the keyboard opens, so the bottom of the page is above the
/// keyboard before anything here runs and the honest answer is that nothing is covered. The measure
/// is geometric rather than a reading of the soft-input mode: the visible frame against where the
/// content actually ends. Where the window did move, that difference is nothing and the bar stays
/// put; where a future Android declines to move it, the same subtraction is the height to lift by.
///
/// Deliberately a layout listener and not an insets listener. Setting
/// <c>OnApplyWindowInsetsListener</c> replaces whatever MAUI installed on the same view, and safe
/// areas across the whole app are not worth a formatting bar.
/// </remarks>
public sealed class AndroidKeyboardInsetService
    : Java.Lang.Object, IKeyboardInsetService, ViewTreeObserver.IOnGlobalLayoutListener
{
    private View? _content;

    public AndroidKeyboardInsetService()
    {
        Attach(Platform.CurrentActivity);
        Platform.ActivityStateChanged += OnActivityStateChanged;
    }

    public double Overlap { get; private set; }

    public event EventHandler? OverlapChanged;

    public void OnGlobalLayout()
    {
        if (_content == null) return;

        var visible = new Android.Graphics.Rect();
        _content.GetWindowVisibleDisplayFrame(visible);

        var location = new int[2];
        _content.GetLocationOnScreen(location);
        var bottom = location[1] + _content.Height;

        var density = _content.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0f) density = 1f;

        var overlap = Math.Max(0, (bottom - visible.Bottom) / density);

        // Layout runs on every frame of an animation and on every keystroke that grows the note.
        // Half a unit is below what anyone can see and well above the rounding.
        if (Math.Abs(overlap - Overlap) < 0.5) return;

        Overlap = overlap;
        OverlapChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnActivityStateChanged(object? sender, ActivityStateChangedEventArgs e)
    {
        if (e.State == ActivityState.Resumed) Attach(e.Activity);
    }

    // The activity can be replaced — a rotation with the app in the background, a cold start after
    // the process was reclaimed — and the old view tree goes with it.
    private void Attach(Activity? activity)
    {
        var content = activity?.Window?.DecorView?.FindViewById(Android.Resource.Id.Content);
        if (content == null || ReferenceEquals(content, _content)) return;

        _content?.ViewTreeObserver?.RemoveOnGlobalLayoutListener(this);
        _content = content;
        content.ViewTreeObserver?.AddOnGlobalLayoutListener(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Platform.ActivityStateChanged -= OnActivityStateChanged;
            _content?.ViewTreeObserver?.RemoveOnGlobalLayoutListener(this);
            _content = null;
        }

        base.Dispose(disposing);
    }
}
