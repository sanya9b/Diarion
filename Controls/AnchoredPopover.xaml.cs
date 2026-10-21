using System.Windows.Input;
using Diarion.Helpers;
using Diarion.Resources.Styles;

namespace Diarion.Controls;

/// <summary>
/// A panel that grows out of a button, drawn as one shape with it, over everything below, with a
/// see-through scrim so a tap anywhere else folds it back.
/// <para>
/// The outline wraps the button in a bump and flows into the panel's body through smooth fillets
/// (<see cref="BubbleSurface"/>). Opening, the body runs out of the bump and widens to the width of the
/// cards around it; the content (<see cref="PopoverContent"/>) arrives once there is room. The button
/// never moves: a copy of it (<see cref="HeaderContent"/>) sits exactly over the real one, so on screen
/// the button has simply sprouted a panel. Folding back runs the same way in reverse. Opens downward
/// when there is room, upward otherwise — then the bump is on the body's bottom edge.
/// </para>
/// <para>
/// Page-level: put one in the page's root grid spanning every row, give it the BindingContext of the
/// control it serves, bind <see cref="IsOpen"/> and <see cref="CloseCommand"/>, and hand it an
/// <see cref="AnchorResolver"/> from code-behind. The anchor is asked for, not remembered: a stored
/// rectangle goes stale the moment anything above the button changes height.
/// </para>
/// </summary>
public partial class AnchoredPopover : Grid
{
    /// <summary>The page margin the cards keep; the body is exactly as wide as a card.</summary>
    private const double PageMargin = 24;

    private const double PadH = 14;
    private const double PadV = 12;

    /// <summary>Room between the button and the bump's outline, enough for a lifted face.</summary>
    private const double BumpPad = 7;

    /// <summary>
    /// The body's edge runs this far past the button's own edge: the whole button stands above the
    /// body and only the bump's last rim dips into it. Through the button's middle, the edge cut the
    /// neighbouring buttons in the same row in half (owner's feedback, 2026-10-21).
    /// </summary>
    private const double JoinGap = 4;

    /// <summary>The panel never touches the top or bottom edge of the page.</summary>
    private const double EdgeMargin = 12;

    /// <summary>Children of the first wrapping row beyond this many enter together; a long stagger would feel like loading.</summary>
    private const int StaggeredItems = 8;

    private const int StaggerStepMs = 30;

    /// <summary>The content waits this long into the growth, until the body is most of its size.</summary>
    private const int BodyDelayMs = 110;

    private const string GrowAnimation = "AnchoredPopoverGrow";

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(AnchoredPopover), false, propertyChanged: OnIsOpenChanged);

    public static readonly BindableProperty AnchorKeyProperty = BindableProperty.Create(
        nameof(AnchorKey), typeof(object), typeof(AnchoredPopover), null, propertyChanged: OnAnchorKeyChanged);

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(ICommand), typeof(AnchoredPopover));

    public static readonly BindableProperty PopoverContentProperty = BindableProperty.Create(
        nameof(PopoverContent), typeof(View), typeof(AnchoredPopover),
        propertyChanged: (b, _, n) => { if (b is AnchoredPopover { Body: not null } p) p.Body.Content = n as View; });

    public static readonly BindableProperty HeaderContentProperty = BindableProperty.Create(
        nameof(HeaderContent), typeof(View), typeof(AnchoredPopover),
        propertyChanged: (b, _, n) => { if (b is AnchoredPopover { HeaderSlot: not null } p) p.HeaderSlot.Content = n as View; });

    private bool _placing;

    public AnchoredPopover()
    {
        InitializeComponent();

        // The layer is collapsed until it opens, so the first placement may run before it has a size,
        // and the content can change height while open. Either way, place again once layout settles.
        SizeChanged += (_, _) => Replace();
        Body.SizeChanged += (_, _) => Replace();
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Bind to whatever names the button the panel belongs to. When it changes while the panel is out,
    /// the panel re-seats itself on the new button with its new content.
    /// </summary>
    public object? AnchorKey
    {
        get => GetValue(AnchorKeyProperty);
        set => SetValue(AnchorKeyProperty, value);
    }

    /// <summary>Run by a tap on the scrim or on the button's copy. The owner decides what closing means.</summary>
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>What the panel holds, in its body.</summary>
    public View? PopoverContent
    {
        get => (View?)GetValue(PopoverContentProperty);
        set => SetValue(PopoverContentProperty, value);
    }

    /// <summary>
    /// A copy of the button, drawn exactly where the button is. It must look like the button — same
    /// shape, same bindings — because it is what the eye takes for the button while the panel is out.
    /// </summary>
    public View? HeaderContent
    {
        get => (View?)GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    /// <summary>
    /// The button's bounds in page coordinates (see <see cref="VisualTreeGeometry.BoundsOnPage"/>), or
    /// null when it is not on screen. Asked every time the panel is placed.
    /// </summary>
    public Func<Rect?>? AnchorResolver { get; set; }

    /// <summary>
    /// A part of the page, in page coordinates, that stays live while the panel is out: taps there reach
    /// the page instead of folding the panel. The mood row uses it so another face is one tap, not two
    /// (owner's feedback, 2026-10-21). Null: the scrim covers everything.
    /// </summary>
    public Func<Rect?>? PassThroughResolver { get; set; }

    private static async void OnIsOpenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not AnchoredPopover popover) return;

        if ((bool)newValue) await popover.OpenAsync();
        else await popover.CloseAsync();
    }

    private static async void OnAnchorKeyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AnchoredPopover { IsOpen: true, IsVisible: true } popover) await popover.ReseatAsync();
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e) => RequestClose();

    private void OnHeaderTapped(object? sender, TappedEventArgs e) => RequestClose();

    private void RequestClose()
    {
        if (CloseCommand?.CanExecute(null) == true) CloseCommand.Execute(null);
    }

    /// <summary>The button's rectangle in this layer's coordinates, asked for now; null if it is gone.</summary>
    private Rect? ResolveAnchor()
    {
        if (AnchorResolver?.Invoke() is not Rect anchor || anchor.Width <= 0 || anchor.Height <= 0) return null;

        var layer = VisualTreeGeometry.BoundsOnPage(this);
        return new Rect(anchor.X - layer.X, anchor.Y - layer.Y, anchor.Width, anchor.Height);
    }

    /// <summary>
    /// Lays the panel out around the button: the bump round it, the body card-wide below it (or above,
    /// when the page runs out of room below), the copy of the button over the button, the content in
    /// the body. Hands the shape to the surface; returns false when the layer has no size yet.
    /// </summary>
    private bool Place(Rect? anchor)
    {
        // Collapsed until the first open, the layer may not have been measured yet; its parent (the
        // page's root grid, which it spans) has the size it is about to get.
        var layerWidth = Width > 0 ? Width : (Parent as VisualElement)?.Width ?? 0;
        var layerHeight = Height > 0 ? Height : (Parent as VisualElement)?.Height ?? 0;
        if (layerWidth <= 0 || layerHeight <= 0) return false;

        _placing = true;
        try
        {
            var bodyLeft = PageMargin;
            var bodyRight = layerWidth - PageMargin;

            Rect? bump = anchor is Rect a
                ? new Rect(a.X - BumpPad, a.Y - BumpPad, a.Width + 2 * BumpPad, a.Height + 2 * BumpPad)
                : null;

            // A button near the card's edge: the body widens just enough to hold the bump and its fillet.
            if (bump is Rect bb)
            {
                bodyLeft = Math.Max(6, Math.Min(bodyLeft, bb.Left - 8));
                bodyRight = Math.Min(layerWidth - 6, Math.Max(bodyRight, bb.Right + 8));
            }

            var contentWidth = Math.Max(1, bodyRight - bodyLeft - 2 * PadH);
            Body.WidthRequest = contentWidth;
            var contentHeight = Body.Measure(contentWidth, double.PositiveInfinity).Height;

            Rect bodyFull;
            Rect bodyStart;
            double contentTop;
            var opensDown = true;

            if (bump is Rect b)
            {
                var r = b.Height / 2;
                var cy = b.Center.Y;

                // Down: the body's top edge runs just under the button, inside the bump's rim; the content
                // starts clear of the bump.
                var downEdge = Math.Min(anchor!.Value.Bottom + JoinGap, cy + r - 2);
                var downContentTop = Math.Max(downEdge + PadV, b.Bottom + 4);
                var downBottom = downContentTop + contentHeight + PadV;

                // Up: mirrored — the body's bottom edge just over the button.
                var upEdge = Math.Max(anchor.Value.Top - JoinGap, cy - r + 2);
                var upContentBottom = Math.Min(upEdge - PadV, b.Top - 4);
                var upTop = upContentBottom - contentHeight - PadV;

                var fitsDown = downBottom <= layerHeight - EdgeMargin;
                var fitsUp = upTop >= EdgeMargin;
                opensDown = fitsDown || !fitsUp;

                // The body starts as a short base just past the bump, a little wider than it.
                var startWidth = Math.Min(bodyRight - bodyLeft, b.Width + 48);
                var startLeft = Math.Clamp(b.Center.X - startWidth / 2, bodyLeft, bodyRight - startWidth);

                if (opensDown)
                {
                    bodyFull = new Rect(bodyLeft, downEdge, bodyRight - bodyLeft, downBottom - downEdge);
                    bodyStart = new Rect(startLeft, downEdge, startWidth, b.Bottom + 10 - downEdge);
                    contentTop = downContentTop;
                }
                else
                {
                    bodyFull = new Rect(bodyLeft, upTop, bodyRight - bodyLeft, upEdge - upTop);
                    bodyStart = new Rect(startLeft, b.Top - 10, startWidth, upEdge - (b.Top - 10));
                    contentTop = upTop + PadV;
                }

                HeaderSlot.IsVisible = true;
                HeaderSlot.WidthRequest = anchor!.Value.Width;
                HeaderSlot.HeightRequest = anchor.Value.Height;
                HeaderSlot.TranslationX = anchor.Value.X;
                HeaderSlot.TranslationY = anchor.Value.Y;
            }
            else
            {
                var height = contentHeight + 2 * PadV;
                var top = Math.Max(EdgeMargin, (layerHeight - height) / 2);
                bodyFull = new Rect(bodyLeft, top, bodyRight - bodyLeft, height);
                bodyStart = bodyFull;
                contentTop = top + PadV;
                HeaderSlot.IsVisible = false;
            }

            Surface.SetGeometry(bump, bodyStart, bodyFull, opensDown);
            LayoutScrim(layerWidth, layerHeight);

            Body.TranslationX = bodyLeft + PadH;
            Body.TranslationY = contentTop;

            BodyHit.WidthRequest = bodyFull.Width;
            BodyHit.HeightRequest = bodyFull.Height;
            BodyHit.TranslationX = bodyFull.X;
            BodyHit.TranslationY = bodyFull.Y;
            return true;
        }
        finally
        {
            _placing = false;
        }
    }

    /// <summary>The scrim's four pieces around the pass-through hole, or the top piece over everything.</summary>
    private void LayoutScrim(double width, double height)
    {
        Rect? hole = null;
        if (PassThroughResolver?.Invoke() is Rect page && page.Width > 0 && page.Height > 0)
        {
            var layer = VisualTreeGeometry.BoundsOnPage(this);
            var x = Math.Clamp(page.X - layer.X, 0, width);
            var y = Math.Clamp(page.Y - layer.Y, 0, height);
            hole = new Rect(x, y, Math.Clamp(page.Width, 0, width - x), Math.Clamp(page.Height, 0, height - y));
        }

        if (hole is not Rect h)
        {
            Put(ScrimTop, 0, 0, width, height);
            Put(ScrimBottom, 0, 0, 0, 0);
            Put(ScrimLeft, 0, 0, 0, 0);
            Put(ScrimRight, 0, 0, 0, 0);
            return;
        }

        Put(ScrimTop, 0, 0, width, h.Top);
        Put(ScrimBottom, 0, h.Bottom, width, height - h.Bottom);
        Put(ScrimLeft, 0, h.Top, h.Left, h.Height);
        Put(ScrimRight, h.Right, h.Top, width - h.Right, h.Height);
    }

    private static void Put(VisualElement piece, double x, double y, double width, double height)
    {
        piece.IsVisible = width > 0 && height > 0;
        piece.TranslationX = x;
        piece.TranslationY = y;
        piece.WidthRequest = Math.Max(0, width);
        piece.HeightRequest = Math.Max(0, height);
    }

    private Task GrowToAsync(double target, int length)
    {
        var done = new TaskCompletionSource();
        Surface.AbortAnimation(GrowAnimation);

        new Animation(v => Surface.Progress = v, Surface.Progress, target)
            .Commit(Surface, GrowAnimation, 16, (uint)length, Motion.EaseOut, (_, _) => done.TrySetResult());

        return done.Task;
    }

    /// <summary>Re-places a panel that is out and at rest, after a layout change.</summary>
    private void Replace()
    {
        if (_placing || !IsOpen || !IsVisible || Surface.AnimationIsRunning(GrowAnimation)) return;

        if (Place(ResolveAnchor())) Surface.Progress = 1;
    }

    /// <summary>
    /// Purpose: spatial sequence. The bump draws round the button and the body runs out of it, widening
    /// into the panel, while the button's copy stays put on the real button; the content arrives once
    /// there is room, its items in a short stagger. Under reduce motion nothing grows; it fades in.
    /// </summary>
    private async Task OpenAsync()
    {
        var reopening = IsVisible;
        IsVisible = true;

        var anchor = ResolveAnchor();
        if (!Place(anchor))
        {
            // No size yet: the SizeChanged that follows places it.
            Surface.Progress = 1;
            Surface.Opacity = 1;
            Body.Opacity = 1;
            return;
        }

        if (Motion.ReduceMotion || anchor is null)
        {
            Surface.AbortAnimation(GrowAnimation);
            Surface.Progress = 1;
            await Task.WhenAll(
                Surface.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut),
                Body.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut),
                StaggerItemsInAsync());
            return;
        }

        if (!reopening)
        {
            Surface.Progress = 0;
            Surface.Opacity = 0;
            Body.Opacity = 0;
        }

        await Task.WhenAll(
            GrowToAsync(1, Motion.OverlayMs),
            Surface.FadeToAsync(1, Motion.FastMs, Motion.EaseOut),
            RevealBodyAsync());
    }

    private async Task RevealBodyAsync()
    {
        await Task.Delay(BodyDelayMs);
        if (!IsOpen) return;

        await Task.WhenAll(Body.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut), StaggerItemsInAsync());
    }

    /// <summary>
    /// The way it came, faster: the content goes first, the body draws back into the bump — wherever
    /// the button is now — and the copy hands over to the real button underneath.
    /// </summary>
    private async Task CloseAsync()
    {
        if (!IsVisible) return;

        Body.CancelAnimations();
        var anchor = ResolveAnchor();

        if (Motion.ReduceMotion || anchor is null)
        {
            await Task.WhenAll(
                Surface.FadeToAsync(0, Motion.FastMs, Motion.EaseOut),
                Body.FadeToAsync(0, Motion.FastMs, Motion.EaseOut));
        }
        else
        {
            // Re-seat on the button's current place first, so the body shrinks back into the button and
            // not into where the button was when it opened.
            var progress = Surface.Progress;
            Place(anchor);
            Surface.Progress = progress;

            await Task.WhenAll(
                Body.FadeToAsync(0, Motion.FastMs / 2, Motion.EaseOut),
                GrowToAsync(0, Motion.StandardMs),
                Surface.FadeToAsync(0, Motion.StandardMs, Motion.EaseOut));
        }

        // Re-opened while closing: the open animation owns the panel now.
        if (IsOpen) return;

        IsVisible = false;
        Surface.Progress = 1;
    }

    /// <summary>
    /// Another button of the row was chosen while the panel was out. The panel stands at the new button
    /// at once and only its content settles — purpose: feedback, the list is a different one now. The
    /// bump does not travel: carrying the copy along made the old button look as if it drove over to
    /// the new one, which is not what happened (owner, 2026-10-21).
    /// </summary>
    private async Task ReseatAsync()
    {
        if (!Place(ResolveAnchor())) return;
        Surface.AbortAnimation(GrowAnimation);
        Surface.Progress = 1;

        Body.CancelAnimations();
        Body.Opacity = 0.4;
        await Task.WhenAll(Body.FadeToAsync(1, Motion.FastMs, Motion.EaseOut), StaggerItemsInAsync());
    }

    /// <summary>The first wrapping row inside the content is a list the user reads; it enters item by item.</summary>
    private Task StaggerItemsInAsync()
    {
        var row = FindFlexLayout(PopoverContent);
        if (row == null) return Task.CompletedTask;

        var tasks = new List<Task>();
        var index = 0;
        foreach (var item in row.Children.OfType<VisualElement>())
        {
            item.CancelAnimations();
            item.Opacity = 0;
            tasks.Add(FadeInAfterAsync(item, Math.Min(index, StaggeredItems) * StaggerStepMs));
            index++;
        }

        return Task.WhenAll(tasks);
    }

    private static FlexLayout? FindFlexLayout(IView? view) => view switch
    {
        null => null,
        FlexLayout flex => flex,
        ContentView content => FindFlexLayout(content.Content),
        Border border => FindFlexLayout(border.Content),
        Layout layout => layout.Children.Select(FindFlexLayout).FirstOrDefault(found => found != null),
        _ => null
    };

    private static async Task FadeInAfterAsync(VisualElement item, int delayMs)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        await item.FadeToAsync(1, Motion.FastMs, Motion.EaseOut);
    }
}
