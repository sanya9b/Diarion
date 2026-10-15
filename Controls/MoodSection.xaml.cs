using System.ComponentModel;
using Diarion.Helpers;
using Diarion.Resources.Styles;
using Diarion.ViewModels;

namespace Diarion.Controls;

/// <summary>
/// The mood row. The bubble of precise states is not here: it lives in <see cref="MoodRefinePopover"/>,
/// a page-level layer, so it can float over the cards below and be dismissed by a tap anywhere else.
/// This side's job is to keep <see cref="DiaryEntryViewModel.RefineAnchor"/> pointing at the chosen
/// face — after selection changes, layout, and scrolling — to slide the one-word caption under that
/// face, and to put the bubble away when the page scrolls, because an anchored bubble that stays while
/// its face moves is wrong twice over.
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
            PublishAnchor();
            PlaceCaption(animated: false);
        };
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = BindingContext as DiaryEntryViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        PublishAnchor();
        PlaceCaption(animated: false);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _scrollView = VisualTreeGeometry.FindScrollView(this);
        if (_scrollView != null) _scrollView.Scrolled += OnScrolled;
        PublishAnchor();
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
        PublishAnchor();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectedColumn is raised before IsRefinementOpen flips, so the popover always finds a fresh
        // anchor by the time it opens. It is also raised before HasCurrentMood, so the caption still
        // knows whether it was on screen before this change.
        if (e.PropertyName == nameof(DiaryEntryViewModel.SelectedColumn))
        {
            PublishAnchor();
            PlaceCaption(animated: true);
        }
    }

    /// <summary>The chosen face's bounds on the page, into the shared view model.</summary>
    private void PublishAnchor()
    {
        if (_viewModel == null) return;

        var column = _viewModel.SelectedColumn;
        if (column < 0 || column >= FacesRow.Children.Count) return;
        if (FacesRow.Children[column] is not VisualElement face || face.Width <= 0) return;

        _viewModel.RefineAnchor = VisualTreeGeometry.BoundsOnPage(face);
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
            _ = Caption.TranslateToAsync(Motion.ReduceMotion ? target : target, 0, Motion.FastMs, Motion.EaseOut);
        }
        else
        {
            Caption.TranslationX = target;
        }
    }
}
