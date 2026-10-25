using System.ComponentModel;
using Diarion.Helpers;
using Diarion.Resources.Styles;
using Diarion.ViewModels;

namespace Diarion.Controls;

/// <summary>
/// The mood row. Its two bubbles — the precise states under the chosen face and the hour grid off the
/// hours chip — are not here: each is an <see cref="AnchoredPopover"/> on the page, so they float over
/// the cards below and a tap anywhere else puts them away. This side answers where they come from
/// (<see cref="GetRefineAnchor"/>, <see cref="GetHourChipAnchor"/>), asked at the moment a bubble is
/// placed, slides the one-word caption under the chosen face, and puts both bubbles away when the page
/// scrolls, because an anchored bubble that stays while its anchor moves is wrong twice over.
/// </summary>
public partial class MoodSection : ContentView
{
    private const int FaceCount = 5;

    private DiaryEntryViewModel? _viewModel;
    private ScrollView? _scrollView;

    public MoodSection()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        FacesRow.SizeChanged += (_, _) => PlaceCaption(animated: false);

        // A new word changes the caption's width; keep it inside the card without disturbing a slide.
        Caption.SizeChanged += (_, _) =>
        {
            var clamped = ClampCaption(Caption.TranslationX);
            if (Math.Abs(clamped - Caption.TranslationX) > 0.5) Caption.TranslationX = clamped;
        };
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = BindingContext as DiaryEntryViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        PlaceCaption(animated: false);
    }

    /// <summary>
    /// Where the precise-states panel grows from: the chosen face's disc, which the panel's copy of the
    /// face covers exactly while it is out. Page coordinates; null while the row is not laid out.
    /// </summary>
    public Rect? GetRefineAnchor()
    {
        if (_viewModel == null) return null;

        var column = _viewModel.SelectedColumn;
        if (column < 0 || column >= FacesRow.Children.Count) return null;
        if (FacesRow.Children[column] is not Border cell || cell.Width <= 0) return null;

        var disc = cell.Content as VisualElement ?? cell;
        return disc.Width > 0 ? VisualTreeGeometry.BoundsOnPage(disc) : null;
    }

    /// <summary>The row of faces in page coordinates: it stays live while the refinement panel is out.</summary>
    public Rect? GetFacesRowBounds() =>
        FacesRow.Width > 0 ? VisualTreeGeometry.BoundsOnPage(FacesRow) : null;

    /// <summary>Where the hour panel grows from: the hours chip, which its copy covers. Page coordinates; null while not laid out.</summary>
    public Rect? GetHourChipAnchor() =>
        HourChip.Width > 0 ? VisualTreeGeometry.BoundsOnPage(HourChip) : null;

    private void OnLoaded(object? sender, EventArgs e)
    {
        _scrollView = VisualTreeGeometry.FindScrollView(this);
        if (_scrollView != null) _scrollView.Scrolled += OnScrolled;
        PlaceCaption(animated: false);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_scrollView != null) _scrollView.Scrolled -= OnScrolled;
        _scrollView = null;
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_viewModel == null) return;

        if (_viewModel.IsRefinementOpen) _viewModel.CloseRefinementCommand.Execute(null);
        if (_viewModel.IsHourPickerOpen) _viewModel.CloseHourPickerCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectedColumn is raised before the caption's text changes, so the slide is placed once the
        // new word is in: its width decides how far toward the card's edge it may go.
        if (e.PropertyName == nameof(DiaryEntryViewModel.SelectedColumn))
        {
            Dispatcher.Dispatch(() => PlaceCaption(animated: true));
        }

        // The hours chip changes width when an hour is chosen (the hour joins the clock) and the ✕ beside
        // it appears. iPhone did not always re-measure the header row for a change that deep, so the
        // chip kept its old width and its clock slid sideways (owner, build 18, 2026-10-25). Asked for
        // explicitly, from the chip up to the row.
        if (e.PropertyName is nameof(DiaryEntryViewModel.HasHourChipBadge)
            or nameof(DiaryEntryViewModel.HourChipBadge)
            or nameof(DiaryEntryViewModel.IsHourSelected))
        {
            ((IView)HourChip).InvalidateMeasure();
            ((IView)Header).InvalidateMeasure();
        }
    }

    /// <summary>
    /// Slides the one word under the chosen face. Purpose: spatial sequence — the word follows the tap.
    /// A word that was not on screen simply appears in place; only a word that moves is animated. Under
    /// the outer faces a long word is held inside the card instead of centred past its edge, where the
    /// card would clip it.
    /// </summary>
    private void PlaceCaption(bool animated)
    {
        if (_viewModel == null || FacesRow.Width <= 0) return;

        var cell = FacesRow.Width / FaceCount;
        var target = ClampCaption((_viewModel.SelectedColumn - (FaceCount - 1) / 2.0) * cell);

        Caption.CancelAnimations();
        if (animated && Caption.IsLoaded && Caption.IsVisible)
        {
            _ = Caption.TranslateToAsync(target, 0, Motion.FastMs, Motion.EaseOut);
        }
        else
        {
            Caption.TranslationX = target;
        }
    }

    /// <summary>How far from the centre the caption can go and still be whole on the card.</summary>
    private double ClampCaption(double offset)
    {
        var row = (Caption.Parent as VisualElement)?.Width ?? 0;
        if (row <= 0) row = FacesRow.Width;
        if (row <= 0) return offset;

        var label = Caption.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        var room = Math.Max(0, (row - label) / 2);
        return Math.Clamp(offset, -room, room);
    }
}
