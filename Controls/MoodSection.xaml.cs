using System.ComponentModel;
using Diarion.Helpers;
using Diarion.Resources.Styles;
using Diarion.ViewModels;

namespace Diarion.Controls;

/// <summary>
/// The mood row. Its two bubbles — the precise states under the chosen face and the hour grid off the
/// hours chip — are not here: each is an <see cref="AnchoredPopover"/> on the page, so they float over
/// the cards below and a tap anywhere else puts them away. This side keeps the two anchors
/// (<see cref="DiaryEntryViewModel.RefineAnchor"/>, <see cref="DiaryEntryViewModel.HourAnchor"/>)
/// pointing at the chosen face and the chip after selection changes, layout and scrolling, slides the
/// one-word caption under the face, and puts both bubbles away when the page scrolls, because an
/// anchored bubble that stays while its anchor moves is wrong twice over.
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
        FacesRow.SizeChanged += (_, _) =>
        {
            PublishAnchors();
            PlaceCaption(animated: false);
        };
        HourChip.SizeChanged += (_, _) => PublishAnchors();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = BindingContext as DiaryEntryViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        PublishAnchors();
        PlaceCaption(animated: false);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _scrollView = VisualTreeGeometry.FindScrollView(this);
        if (_scrollView != null) _scrollView.Scrolled += OnScrolled;
        PublishAnchors();
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
        PublishAnchors();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectedColumn is raised before IsRefinementOpen flips, so the popover always finds a fresh
        // anchor by the time it opens. It is also raised before HasCurrentMood, so the caption still
        // knows whether it was on screen before this change.
        if (e.PropertyName == nameof(DiaryEntryViewModel.SelectedColumn))
        {
            PublishAnchors();
            PlaceCaption(animated: true);
        }
        else if (e.PropertyName == nameof(DiaryEntryViewModel.HourChipText))
        {
            // The chip's text changed its width; its bubble's tail follows once layout settles.
            Dispatcher.Dispatch(PublishAnchors);
        }
    }

    /// <summary>The chosen face's and the hours chip's bounds on the page, into the shared view model.</summary>
    private void PublishAnchors()
    {
        if (_viewModel == null) return;

        var column = _viewModel.SelectedColumn;
        if (column >= 0 && column < FacesRow.Children.Count
            && FacesRow.Children[column] is VisualElement face && face.Width > 0)
        {
            // The bubble hangs below the one-word caption, not over it: the anchor is the face plus
            // the caption row beneath.
            var bounds = VisualTreeGeometry.BoundsOnPage(face);
            var captionRow = Caption.Parent as VisualElement;
            var captionHeight = captionRow?.Height > 0 ? captionRow.Height + 2 : 0;
            _viewModel.RefineAnchor = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height + captionHeight);
        }

        if (HourChip.Width > 0)
        {
            _viewModel.HourAnchor = VisualTreeGeometry.BoundsOnPage(HourChip);
        }
    }

    /// <summary>
    /// Slides the one word under the chosen face. Purpose: spatial sequence — the word follows the tap.
    /// A word that was not on screen simply appears in place; only a word that moves is animated.
    /// </summary>
    private void PlaceCaption(bool animated)
    {
        if (_viewModel == null || FacesRow.Width <= 0) return;

        var cell = FacesRow.Width / FaceCount;
        var target = (_viewModel.SelectedColumn - (FaceCount - 1) / 2.0) * cell;

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
}
