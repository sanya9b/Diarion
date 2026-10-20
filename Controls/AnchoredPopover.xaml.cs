using System.Windows.Input;
using Diarion.Helpers;
using Diarion.Resources.Styles;

namespace Diarion.Controls;

/// <summary>
/// A bubble that grows out of a control somewhere on the page and floats over everything below it,
/// with a see-through scrim so a tap anywhere else puts it away.
/// <para>
/// Page-level: put one in the page's root grid spanning every row, give it the same BindingContext as
/// the control it serves, bind <see cref="IsOpen"/> and <see cref="CloseCommand"/>, and hand it an
/// <see cref="AnchorResolver"/> from code-behind. What is inside is <see cref="PopoverContent"/>, set as
/// a property element.
/// </para>
/// <para>
/// The anchor is asked for, not remembered. A stored rectangle goes stale the moment anything above the
/// control changes height — a strip appearing, a banner, the calendar opening — and the bubble then
/// lands on whatever card has moved into the old spot. Asking at the moment of placement cannot be
/// stale.
/// </para>
/// </summary>
public partial class AnchoredPopover : ContentView
{
    /// <summary>Space between the control and the bubble.</summary>
    private const double Gap = 6;

    /// <summary>The bubble never touches the top or bottom edge of the page.</summary>
    private const double EdgeMargin = 12;

    /// <summary>Children of the first wrapping row beyond this many enter together; a long stagger would feel like loading.</summary>
    private const int StaggeredItems = 8;

    private const int StaggerStepMs = 30;

    /// <summary>The content waits this long into the growth, until the card is most of its size.</summary>
    private const int BodyDelayMs = 70;

    private const string MorphAnimation = "AnchoredPopoverMorph";

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(AnchoredPopover), false, propertyChanged: OnIsOpenChanged);

    public static readonly BindableProperty AnchorKeyProperty = BindableProperty.Create(
        nameof(AnchorKey), typeof(object), typeof(AnchoredPopover), null, propertyChanged: OnAnchorKeyChanged);

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(ICommand), typeof(AnchoredPopover));

    public static readonly BindableProperty PopoverContentProperty = BindableProperty.Create(
        nameof(PopoverContent), typeof(View), typeof(AnchoredPopover), propertyChanged: OnPopoverContentChanged);

    // Morph state: 0 is the control's rectangle, 1 is the bubble's own place. Kept as fields so an
    // interrupted open or close continues from where it is instead of jumping to an end.
    private double _progress;
    private Rect _from;
    private Rect _to;

    public AnchoredPopover()
    {
        InitializeComponent();

        // The layer is collapsed until it opens, so the first placement may run before it has a size,
        // and the content can change height while open. Either way, place again once layout settles.
        SizeChanged += (_, _) => Replace();
        Bubble.SizeChanged += (_, _) => Replace();
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Bind to whatever names the control the bubble belongs to — the chosen mood, say. When it changes
    /// while the bubble is out, the bubble moves to the new control and settles there with its new
    /// content, instead of leaving and re-entering.
    /// </summary>
    public object? AnchorKey
    {
        get => GetValue(AnchorKeyProperty);
        set => SetValue(AnchorKeyProperty, value);
    }

    /// <summary>Run by a tap on the scrim. The owner decides what closing means.</summary>
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public View? PopoverContent
    {
        get => (View?)GetValue(PopoverContentProperty);
        set => SetValue(PopoverContentProperty, value);
    }

    /// <summary>
    /// The control's bounds in page coordinates (see <see cref="VisualTreeGeometry.BoundsOnPage"/>), or
    /// null when it is not on screen. Asked every time the bubble is placed.
    /// </summary>
    public Func<Rect?>? AnchorResolver { get; set; }

    private static void OnPopoverContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AnchoredPopover { Body: not null } popover) popover.Body.Content = newValue as View;
    }

    private static async void OnIsOpenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not AnchoredPopover popover) return;

        if ((bool)newValue) await popover.OpenAsync();
        else await popover.CloseAsync();
    }

    private static async void OnAnchorKeyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AnchoredPopover { IsOpen: true, IsVisible: true } popover) await popover.MoveAsync();
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e)
    {
        if (CloseCommand?.CanExecute(null) == true) CloseCommand.Execute(null);
    }

    /// <summary>The control's rectangle in this layer's coordinates, asked for now; null if it is gone.</summary>
    private Rect? ResolveAnchor()
    {
        if (AnchorResolver?.Invoke() is not Rect anchor || anchor.Width <= 0 || anchor.Height <= 0) return null;

        var layer = VisualTreeGeometry.BoundsOnPage(this);
        return new Rect(anchor.X - layer.X, anchor.Y - layer.Y, anchor.Width, anchor.Height);
    }

    /// <summary>
    /// Where the bubble rests: full width between the page margins, under the control, or above it when
    /// the page runs out of room below, and never past the top or bottom edge. Computes <c>_to</c>;
    /// returns false when the layer has no size yet.
    /// </summary>
    private bool Place(Rect? anchor)
    {
        // Collapsed until the first open, the layer may not have been measured yet; its parent (the
        // page's root grid, which it spans) has the size it is about to get.
        var layerWidth = Width > 0 ? Width : (Parent as VisualElement)?.Width ?? 0;
        var layerHeight = Height > 0 ? Height : (Parent as VisualElement)?.Height ?? 0;
        if (layerWidth <= 0 || layerHeight <= 0) return false;

        var width = Math.Max(1, layerWidth - Bubble.Margin.HorizontalThickness);
        var height = Bubble.Height > 0 && Math.Abs(Bubble.Width - width) < 0.5
            ? Bubble.Height
            : Bubble.Measure(width, double.PositiveInfinity).Height;

        double top;
        if (anchor is Rect a)
        {
            var below = a.Bottom + Gap;
            var above = a.Top - Gap - height;
            top = below + height <= layerHeight - EdgeMargin || above < EdgeMargin ? below : above;
        }
        else
        {
            top = (layerHeight - height) / 2;
        }

        top = Math.Clamp(top, EdgeMargin, Math.Max(EdgeMargin, layerHeight - height - EdgeMargin));
        _to = new Rect(Bubble.Margin.Left, top, width, height);
        return true;
    }

    /// <summary>
    /// Draws the bubble at <c>_progress</c> between the control's rectangle and its own. The card's
    /// laid-out box is always the final one; translation and scale (about its top-left corner) map it
    /// onto the in-between rectangle, which is what makes it look pulled out of the control.
    /// </summary>
    private void ApplyMorph()
    {
        if (_to.Width <= 0 || _to.Height <= 0) return;

        var t = _progress;
        var x = Lerp(_from.X, _to.X, t);
        var y = Lerp(_from.Y, _to.Y, t);
        var w = Math.Max(1, Lerp(_from.Width, _to.Width, t));
        var h = Math.Max(1, Lerp(_from.Height, _to.Height, t));

        Bubble.TranslationX = x - _to.X;
        Bubble.TranslationY = y;
        Bubble.ScaleX = w / _to.Width;
        Bubble.ScaleY = h / _to.Height;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private Task MorphToAsync(double target, int length)
    {
        var done = new TaskCompletionSource();
        Bubble.AbortAnimation(MorphAnimation);

        new Animation(v => { _progress = v; ApplyMorph(); }, _progress, target)
            .Commit(Bubble, MorphAnimation, 16, (uint)length, Motion.EaseOut, (_, _) => done.TrySetResult());

        return done.Task;
    }

    /// <summary>Re-places a bubble that is out and at rest, after a layout change.</summary>
    private void Replace()
    {
        if (!IsOpen || !IsVisible || Bubble.AnimationIsRunning(MorphAnimation)) return;

        var anchor = ResolveAnchor();
        if (!Place(anchor)) return;
        if (anchor is Rect a) _from = a;
        _progress = 1;
        ApplyMorph();
    }

    /// <summary>
    /// Purpose: spatial sequence. The card starts as the control's own rectangle and grows into place,
    /// so what is inside reads as having come out of the tap. The content follows a beat later and its
    /// items in a short stagger. Under reduce motion nothing moves; the bubble fades in where it rests.
    /// </summary>
    private async Task OpenAsync()
    {
        var reopening = IsVisible;
        IsVisible = true;

        var anchor = ResolveAnchor();
        if (!Place(anchor))
        {
            // No size yet: the SizeChanged that follows places it.
            _progress = 1;
            Bubble.Opacity = 1;
            Body.Opacity = 1;
            return;
        }

        if (Motion.ReduceMotion || anchor is null)
        {
            Bubble.AbortAnimation(MorphAnimation);
            _from = anchor ?? _to;
            _progress = 1;
            ApplyMorph();
            if (!reopening) Bubble.Opacity = 0;
            Body.Opacity = 1;
            await Task.WhenAll(Bubble.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut), StaggerItemsInAsync());
            return;
        }

        _from = anchor.Value;
        if (!reopening)
        {
            _progress = 0;
            Bubble.Opacity = 0;
            Body.Opacity = 0;
            ApplyMorph();
        }

        await Task.WhenAll(
            MorphToAsync(1, Motion.OverlayMs),
            Bubble.FadeToAsync(1, Motion.FastMs, Motion.EaseOut),
            RevealBodyAsync());
    }

    private async Task RevealBodyAsync()
    {
        await Task.Delay(BodyDelayMs);
        if (!IsOpen) return;

        await Task.WhenAll(Body.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut), StaggerItemsInAsync());
    }

    /// <summary>
    /// Out the way it came in, faster: back into the control's rectangle — wherever that control is
    /// now — fading as it goes. Without a control to return to, or under reduce motion, it just fades.
    /// </summary>
    private async Task CloseAsync()
    {
        if (!IsVisible) return;

        var anchor = ResolveAnchor();
        Body.CancelAnimations();

        if (Motion.ReduceMotion || anchor is null)
        {
            await Task.WhenAll(Bubble.FadeToAsync(0, Motion.FastMs, Motion.EaseOut), Body.FadeToAsync(0, Motion.FastMs, Motion.EaseOut));
        }
        else
        {
            _from = anchor.Value;
            await Task.WhenAll(
                Body.FadeToAsync(0, Motion.FastMs / 2, Motion.EaseOut),
                MorphToAsync(0, Motion.StandardMs),
                Bubble.FadeToAsync(0, Motion.StandardMs, Motion.EaseOut));
        }

        // Re-opened while closing: the open animation owns the bubble now.
        if (IsOpen) return;

        IsVisible = false;
        _progress = 1;
        Bubble.TranslationX = 0;
        Bubble.ScaleX = 1;
        Bubble.ScaleY = 1;
    }

    /// <summary>
    /// Purpose: state indication. Another control was chosen while the bubble was out: it settles at
    /// the new control's place with the new content — a short dim-and-return, not a second entrance.
    /// </summary>
    private async Task MoveAsync()
    {
        var anchor = ResolveAnchor();
        if (!Place(anchor)) return;

        Bubble.AbortAnimation(MorphAnimation);
        if (anchor is Rect a) _from = a;
        _progress = 1;
        ApplyMorph();

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
