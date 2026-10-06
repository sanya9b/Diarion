using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.ViewModels;

namespace Diarion.Controls;

/// <summary>
/// The coach-mark layer: a scrim with a hole cut around one element, and a card explaining it.
/// One of these per guided screen.
/// </summary>
/// <remarks>
/// The split of duties is the whole point of this control. <see cref="TourViewModel"/> owns the
/// running order and the wording and knows nothing about the screen; this owns the geometry and
/// knows nothing about the steps beyond the <see cref="TourStepId"/> it is handed. What connects
/// the two is <see cref="AnchorResolver"/>, which each page supplies — a page is the only thing
/// that can say which of its elements an id means.
/// </remarks>
public partial class TourOverlayView : ContentView
{
    /// <summary>Breathing room between asking for a layout change and measuring the result.
    /// Scrolling and a tab switch both take time, and a hole cut around where an element used to be
    /// is worse than no hole at all.</summary>
    private static readonly TimeSpan LayoutSettleDelay = TimeSpan.FromMilliseconds(120);

    private const double CalloutGap = 24;
    private const double CalloutSide = 20;

    /// <summary>What to assume the card needs before it has ever been laid out. Roughly a title, two
    /// lines of body and the button row.</summary>
    private const double CalloutAssumedHeight = 170;

    private TourViewModel? _tour;

    public TourOverlayView()
    {
        InitializeComponent();

        // The spotlight is drawn in this layer's coordinates, so anything that resizes it invalidates
        // the hole. Orientation is locked, but the window is resizable on desktop and the keyboard
        // resizes it everywhere else.
        SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// The guide to draw. Assigned from the page's code-behind rather than bound, because the layer
    /// also has to listen to it, and a binding would leave nothing to unsubscribe from.
    /// </summary>
    public TourViewModel? Tour
    {
        get => _tour;
        set
        {
            if (ReferenceEquals(_tour, value)) return;

            if (_tour is not null)
            {
                _tour.PropertyChanged -= OnTourPropertyChanged;
            }

            _tour = value;
            Root.BindingContext = value;
            IsVisible = value?.IsActive == true;

            if (_tour is not null)
            {
                _tour.PropertyChanged += OnTourPropertyChanged;
            }
        }
    }

    /// <summary>
    /// Which element a step points at, answered by the page. Returning <c>null</c> is legitimate —
    /// a card whose target is not on screen is shown without a hole rather than skipped, because
    /// what it says is still true.
    /// </summary>
    public Func<TourStepId, VisualElement?>? AnchorResolver { get; set; }

    /// <summary>
    /// Anything the page must do before a step can be measured: switch to the tab the target lives
    /// on, close a panel, and so on. Awaited before the anchor is resolved.
    /// </summary>
    public Func<TourStep, Task>? PrepareStepAsync { get; set; }

    /// <summary>
    /// The scroller the anchors live in, if they do. Set it and an off-screen target is brought into
    /// view before it is measured; leave it null on a page that does not scroll its targets.
    /// </summary>
    public ScrollView? ScrollHost { get; set; }

    private async void OnSizeChanged(object? sender, EventArgs e) => await PositionAsync();

    private async void OnTourPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TourViewModel.IsActive))
        {
            IsVisible = _tour?.IsActive == true;
        }

        if (e.PropertyName is nameof(TourViewModel.CurrentStep) or nameof(TourViewModel.IsActive))
        {
            await PositionAsync();
        }
    }

    private async Task PositionAsync()
    {
        var step = _tour?.CurrentStep;
        if (step is null) return;

        if (PrepareStepAsync is not null)
        {
            await PrepareStepAsync(step);
            await Task.Delay(LayoutSettleDelay);
        }

        var anchor = AnchorResolver?.Invoke(step.Id);
        if (anchor is not null)
        {
            await BringIntoViewAsync(anchor);
        }

        await Task.Delay(LayoutSettleDelay);

        // Re-read the step: the delays above are long enough for an impatient tap to have moved on,
        // and positioning the previous step's hole over the current step's card is the one visible
        // way this can go wrong.
        if (!ReferenceEquals(_tour?.CurrentStep, step)) return;

        if (anchor is null || anchor.Width <= 0 || anchor.Height <= 0 || !anchor.IsVisible)
        {
            // No hole rather than a wrong one: the scrim and the card still explain the step.
            Spotlight.TargetRect = Rect.Zero;
            return;
        }

        var bounds = BoundsRelativeToLayer(anchor);
        Spotlight.TargetRect = bounds;
        PlaceCallout(bounds);
    }

    private async Task BringIntoViewAsync(VisualElement anchor)
    {
        // ScrollToAsync throws on an element that is not inside the scroller, and a page is free to
        // mix scrolled anchors with fixed ones.
        if (ScrollHost is null || !IsDescendantOf(anchor, ScrollHost)) return;

        await ScrollHost.ScrollToAsync(anchor, ScrollToPosition.Center, animated: false);
    }

    private static bool IsDescendantOf(Element element, Element ancestor)
    {
        for (var current = element.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    /// <summary>Where an element sits in this layer's coordinates, which is the space the spotlight
    /// is drawn in.</summary>
    /// <remarks>
    /// Both the element and the layer are measured against the page root and then subtracted, which
    /// is correct whether or not the layer happens to fill the page. MAUI exposes no "bounds
    /// relative to that ancestor" for a cross-platform layout, and the platform variants each answer
    /// in different units. Scroll offsets come off on the way up, because a <see cref="ScrollView"/>
    /// moves its content without moving its children's <c>X</c>/<c>Y</c>.
    /// </remarks>
    private Rect BoundsRelativeToLayer(VisualElement element)
    {
        var target = OffsetFromRoot(element);
        var layer = OffsetFromRoot(this);

        return new Rect(target.X - layer.X, target.Y - layer.Y, element.Width, element.Height);
    }

    private static Point OffsetFromRoot(VisualElement element)
    {
        double x = 0;
        double y = 0;

        Element? current = element;
        while (current is VisualElement visual)
        {
            x += visual.X;
            y += visual.Y;

            if (visual.Parent is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }

            current = visual.Parent;
        }

        return new Point(x, y);
    }

    /// <summary>Puts the card on whichever side of the highlight can actually hold it.</summary>
    /// <remarks>
    /// Which half of the screen the highlight sits in is not the question — how much room is left
    /// beside it is. A card centred on the settings blocks list is above the middle and still has
    /// nowhere to go: the highlight is 380pt tall on a 675pt page, and choosing "below" pushed the
    /// buttons off the bottom edge where nothing could reach them.
    /// </remarks>
    private void PlaceCallout(Rect hole)
    {
        var needed = (Callout.Height > 0 ? Callout.Height : CalloutAssumedHeight) + CalloutGap + CalloutSide;
        var roomBelow = Height - hole.Bottom;
        var roomAbove = hole.Top;

        if (roomBelow >= needed && roomBelow >= roomAbove)
        {
            Callout.VerticalOptions = LayoutOptions.Start;
            Callout.Margin = new Thickness(CalloutSide, hole.Bottom + CalloutGap, CalloutSide, CalloutSide);
        }
        else if (roomAbove >= needed)
        {
            Callout.VerticalOptions = LayoutOptions.End;
            Callout.Margin = new Thickness(CalloutSide, CalloutSide, CalloutSide, Height - hole.Top + CalloutGap);
        }
        else
        {
            // A highlight taller than the room around it leaves no clear side. The card takes the
            // bottom edge and covers part of it: a covered highlight reads, an unreachable card does not.
            Callout.VerticalOptions = LayoutOptions.End;
            Callout.Margin = new Thickness(CalloutSide);
        }
    }
}
