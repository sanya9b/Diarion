using System.ComponentModel;
using Diarion.Models;
using Diarion.Resources.Styles;
using Diarion.ViewModels;

namespace Diarion.Controls;

/// <summary>
/// The mood row's bubble of precise states, as a page-level layer: it floats over whatever is below
/// the row, grows out of the chosen face, and a tap anywhere else puts it away. Position comes from
/// <see cref="DiaryEntryViewModel.RefineAnchor"/>, which <see cref="MoodSection"/> keeps current.
/// </summary>
public partial class MoodRefinePopover : ContentView
{
    /// <summary>Gap between the face and the bubble's tail.</summary>
    private const double Gap = 6;

    private const double TailWidth = 18;

    /// <summary>Chips beyond this many enter together; a long stagger would feel like loading.</summary>
    private const int StaggeredChips = 8;

    private const int StaggerStepMs = 30;

    private DiaryEntryViewModel? _viewModel;

    /// <summary>The quadrant whose chips the bubble is showing; None while it is hidden.</summary>
    private EmotionQuadrant _shownQuadrant = EmotionQuadrant.None;

    public MoodRefinePopover()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = BindingContext as DiaryEntryViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // A new day arrives with its bubble closed; nothing to animate.
        Bubble.CancelAnimations();
        IsVisible = false;
        Bubble.Opacity = 0;
        Bubble.Scale = 1;
        _shownQuadrant = EmotionQuadrant.None;
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (e.PropertyName)
        {
            case nameof(DiaryEntryViewModel.IsRefinementOpen):
                if (_viewModel.IsRefinementOpen) await OpenAsync();
                else await CloseAsync();
                break;

            case nameof(DiaryEntryViewModel.RefineAnchor) when _viewModel.IsRefinementOpen && IsVisible:
                // The chosen face moved while the bubble was out: another face was tapped.
                if (_shownQuadrant != _viewModel.CurrentQuadrant) await MoveAsync();
                else Place();
                break;
        }
    }

    /// <summary>
    /// Puts the bubble under the chosen face, or above it when the page runs out of room below, and
    /// slides the tail to the face's centre. All in this layer's coordinates, which span the page.
    /// </summary>
    private void Place()
    {
        if (_viewModel == null || Width <= 0) return;

        var anchor = _viewModel.RefineAnchor;
        var layer = Helpers.VisualTreeGeometry.BoundsOnPage(this);
        var bubbleLeft = Bubble.Margin.Left;
        var bubbleWidth = Math.Max(1, Width - Bubble.Margin.HorizontalThickness);

        var measured = Bubble.Measure(bubbleWidth, double.PositiveInfinity);
        var bubbleHeight = measured.Height;

        var below = anchor.Bottom - layer.Y + Gap;
        var fitsBelow = below + bubbleHeight <= Height;
        var above = anchor.Top - layer.Y - Gap - bubbleHeight;
        var placeBelow = fitsBelow || above < 0;

        TailTopRow.IsVisible = placeBelow;
        TailBottomRow.IsVisible = !placeBelow;
        Bubble.TranslationY = placeBelow ? below : above;

        // The tail points at the face; the bubble scales from that same point.
        var tailX = Math.Clamp(anchor.Center.X - layer.X - bubbleLeft - TailWidth / 2, 12, bubbleWidth - TailWidth - 12);
        TailTop.TranslationX = tailX;
        TailBottom.TranslationX = tailX;
        Bubble.AnchorX = (tailX + TailWidth / 2) / bubbleWidth;
        Bubble.AnchorY = placeBelow ? 0 : 1;
    }

    /// <summary>
    /// Purpose: spatial sequence. The bubble grows out of the face that was just tapped and its chips
    /// follow in a short stagger, so the precise states read as a consequence of the tap.
    /// </summary>
    private async Task OpenAsync()
    {
        _shownQuadrant = _viewModel?.CurrentQuadrant ?? EmotionQuadrant.None;
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
            StaggerChipsInAsync());
    }

    /// <summary>Out the way it came in, faster.</summary>
    private async Task CloseAsync()
    {
        _shownQuadrant = EmotionQuadrant.None;
        Bubble.CancelAnimations();

        await Task.WhenAll(
            Bubble.FadeToAsync(0, Motion.FastMs, Motion.EaseOut),
            Bubble.ScaleToAsync(Motion.Scale(0.96), Motion.FastMs, Motion.EaseOut));

        // Re-opened while closing: the open animation owns the bubble now.
        if (_viewModel?.IsRefinementOpen == true) return;

        IsVisible = false;
        Bubble.Scale = 1;
    }

    /// <summary>
    /// Purpose: state indication. The bubble settles under the new face with new chips instead of
    /// leaving and re-entering, which would make every change of face feel like a navigation.
    /// </summary>
    private async Task MoveAsync()
    {
        _shownQuadrant = _viewModel?.CurrentQuadrant ?? EmotionQuadrant.None;
        Bubble.CancelAnimations();
        Place();
        Bubble.Opacity = 0.4;
        Bubble.Scale = Motion.Scale(0.97);

        await Task.WhenAll(
            Bubble.FadeToAsync(1, Motion.FastMs, Motion.EaseOut),
            Bubble.ScaleToAsync(1, Motion.FastMs, Motion.EaseOut),
            StaggerChipsInAsync());
    }

    private Task StaggerChipsInAsync()
    {
        var tasks = new List<Task>();
        var index = 0;
        foreach (var chip in Chips.Children.OfType<VisualElement>())
        {
            chip.CancelAnimations();
            chip.Opacity = 0;
            tasks.Add(FadeInAfterAsync(chip, Math.Min(index, StaggeredChips) * StaggerStepMs));
            index++;
        }

        return Task.WhenAll(tasks);
    }

    private static async Task FadeInAfterAsync(VisualElement chip, int delayMs)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        await chip.FadeToAsync(1, Motion.FastMs, Motion.EaseOut);
    }
}
