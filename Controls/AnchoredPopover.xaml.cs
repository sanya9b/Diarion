using System.Windows.Input;
using Diarion.Helpers;
using Diarion.Resources.Styles;

namespace Diarion.Controls;

/// <summary>
/// A bubble that grows out of a control somewhere on the page and floats over everything below it,
/// with a tail pointing at that control and a see-through scrim so a tap anywhere else puts it away.
/// Page-level: put one in the page's root grid spanning every row, give it the same BindingContext as
/// the control it serves, and bind <see cref="IsOpen"/>, <see cref="Anchor"/> (the control's bounds on
/// the page, see <see cref="VisualTreeGeometry.BoundsOnPage"/>) and <see cref="CloseCommand"/>. What is
/// inside is <see cref="PopoverContent"/>, set as a property element: a ContentProperty redirect would
/// also swallow this control's own XAML root, which is the layer, not the content.
/// </summary>
public partial class AnchoredPopover : ContentView
{
    /// <summary>Gap between the anchor and the bubble's tail.</summary>
    private const double Gap = 6;

    private const double TailWidth = 18;

    /// <summary>Children of the first wrapping row beyond this many enter together; a long stagger would feel like loading.</summary>
    private const int StaggeredItems = 8;

    private const int StaggerStepMs = 30;

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(AnchoredPopover), false, propertyChanged: OnIsOpenChanged);

    public static readonly BindableProperty AnchorProperty = BindableProperty.Create(
        nameof(Anchor), typeof(Rect), typeof(AnchoredPopover), default(Rect), propertyChanged: OnAnchorChanged);

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(ICommand), typeof(AnchoredPopover));

    public static readonly BindableProperty PopoverContentProperty = BindableProperty.Create(
        nameof(PopoverContent), typeof(View), typeof(AnchoredPopover), propertyChanged: OnPopoverContentChanged);

    public AnchoredPopover()
    {
        InitializeComponent();

        // The layer is collapsed until it opens, so the first Place() may run before it has a size;
        // the layout pass that follows corrects the position.
        SizeChanged += (_, _) =>
        {
            if (IsOpen && IsVisible) Place();
        };
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The bounds, on the page, of the control the bubble grows from.</summary>
    public Rect Anchor
    {
        get => (Rect)GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
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

    private static void OnPopoverContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AnchoredPopover { Card: not null } popover) popover.Card.Content = newValue as View;
    }

    private static async void OnIsOpenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not AnchoredPopover popover) return;

        if ((bool)newValue) await popover.OpenAsync();
        else await popover.CloseAsync();
    }

    private static async void OnAnchorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // The anchor moved while the bubble was out: it settles under the new spot with whatever the
        // content now shows, instead of leaving and re-entering.
        if (bindable is AnchoredPopover { IsOpen: true, IsVisible: true } popover) await popover.MoveAsync();
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e)
    {
        if (CloseCommand?.CanExecute(null) == true) CloseCommand.Execute(null);
    }

    /// <summary>
    /// Puts the bubble under the anchor, or above it when the page runs out of room below, and slides
    /// the tail to the anchor's centre. All in this layer's coordinates, which span the page.
    /// </summary>
    private void Place()
    {
        // Collapsed until the first open, the layer may not have been measured yet; its parent
        // (the page's root grid, which it spans) has the size it is about to get.
        var layerWidth = Width > 0 ? Width : (Parent as VisualElement)?.Width ?? 0;
        var layerHeight = Height > 0 ? Height : (Parent as VisualElement)?.Height ?? 0;
        if (layerWidth <= 0 || layerHeight <= 0) return;

        var anchor = Anchor;
        var layer = VisualTreeGeometry.BoundsOnPage(this);
        var bubbleLeft = Bubble.Margin.Left;
        var bubbleWidth = Math.Max(1, layerWidth - Bubble.Margin.HorizontalThickness);

        var bubbleHeight = Bubble.Measure(bubbleWidth, double.PositiveInfinity).Height;

        var below = anchor.Bottom - layer.Y + Gap;
        var fitsBelow = below + bubbleHeight <= layerHeight;
        var above = anchor.Top - layer.Y - Gap - bubbleHeight;
        var placeBelow = fitsBelow || above < 0;

        TailTopRow.IsVisible = placeBelow;
        TailBottomRow.IsVisible = !placeBelow;
        Bubble.TranslationY = placeBelow ? below : above;

        // The tail points at the anchor; the bubble scales from that same point.
        var tailX = Math.Clamp(anchor.Center.X - layer.X - bubbleLeft - TailWidth / 2, 12, Math.Max(12, bubbleWidth - TailWidth - 12));
        TailTop.TranslationX = tailX;
        TailBottom.TranslationX = tailX;
        Bubble.AnchorX = (tailX + TailWidth / 2) / bubbleWidth;
        Bubble.AnchorY = placeBelow ? 0 : 1;
    }

    /// <summary>
    /// Purpose: spatial sequence. The bubble grows out of the control that was just tapped and its
    /// items follow in a short stagger, so what is inside reads as a consequence of the tap.
    /// </summary>
    private async Task OpenAsync()
    {
        Bubble.CancelAnimations();

        var wasVisible = IsVisible;
        IsVisible = true;
        Place();

        if (!wasVisible)
        {
            Bubble.Opacity = 0;
            Bubble.Scale = Motion.Scale(0.92);
        }

        await Task.WhenAll(
            Bubble.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut),
            Bubble.ScaleToAsync(1, Motion.StandardMs, Motion.EaseOut),
            StaggerItemsInAsync());
    }

    /// <summary>Out the way it came in, faster.</summary>
    private async Task CloseAsync()
    {
        Bubble.CancelAnimations();

        await Task.WhenAll(
            Bubble.FadeToAsync(0, Motion.FastMs, Motion.EaseOut),
            Bubble.ScaleToAsync(Motion.Scale(0.96), Motion.FastMs, Motion.EaseOut));

        // Re-opened while closing: the open animation owns the bubble now.
        if (IsOpen) return;

        IsVisible = false;
        Bubble.Scale = 1;
    }

    /// <summary>Purpose: state indication. A short settle says "same bubble, new place or new content".</summary>
    private async Task MoveAsync()
    {
        Bubble.CancelAnimations();
        Place();
        Bubble.Opacity = 0.4;
        Bubble.Scale = Motion.Scale(0.97);

        await Task.WhenAll(
            Bubble.FadeToAsync(1, Motion.FastMs, Motion.EaseOut),
            Bubble.ScaleToAsync(1, Motion.FastMs, Motion.EaseOut),
            StaggerItemsInAsync());
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
