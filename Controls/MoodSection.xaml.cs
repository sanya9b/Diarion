using System.ComponentModel;
using Diarion.Models;
using Diarion.Resources.Styles;
using Diarion.ViewModels;

namespace Diarion.Controls;

public partial class MoodSection : ContentView
{
    private DiaryEntryViewModel? _viewModel;

    /// <summary>The quadrant whose chips the row is currently showing; None while the row is hidden.</summary>
    private EmotionQuadrant _shownQuadrant = EmotionQuadrant.None;

    public MoodSection()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = BindingContext as DiaryEntryViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // A day that arrives with a mood already logged shows its chips at once. Motion is for what
        // the user just did, not for what was already true when the card appeared.
        RefinePanel.CancelAnimations();
        var visible = _viewModel?.HasRefinements == true;
        RefinePanel.IsVisible = visible;
        RefinePanel.Opacity = visible ? 1 : 0;
        RefinePanel.TranslationY = 0;
        _shownQuadrant = visible ? _viewModel!.CurrentQuadrant : EmotionQuadrant.None;
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null) return;

        if (e.PropertyName == nameof(DiaryEntryViewModel.HasRefinements))
        {
            if (_viewModel.HasRefinements) await ShowRefinementsAsync();
            else await HideRefinementsAsync();
        }
        else if (e.PropertyName == nameof(DiaryEntryViewModel.CurrentQuadrant))
        {
            // The row stayed but its chips were swapped for another quadrant's.
            var quadrant = _viewModel.CurrentQuadrant;
            var switched = RefinePanel.IsVisible && _viewModel.HasRefinements && _shownQuadrant != quadrant;
            _shownQuadrant = _viewModel.HasRefinements ? quadrant : EmotionQuadrant.None;
            if (switched) await SettleRefinementsAsync();
        }
    }

    /// <summary>
    /// Purpose: spatial sequence. The chips come out from under the tile that was just tapped, so they
    /// read as a consequence of it rather than as a second, unrelated control appearing.
    /// </summary>
    private async Task ShowRefinementsAsync()
    {
        _shownQuadrant = _viewModel?.CurrentQuadrant ?? EmotionQuadrant.None;
        RefinePanel.CancelAnimations();

        if (!RefinePanel.IsVisible)
        {
            RefinePanel.Opacity = 0;
            RefinePanel.TranslationY = Motion.Offset(-6);
            RefinePanel.IsVisible = true;
        }

        await Task.WhenAll(
            RefinePanel.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut),
            RefinePanel.TranslateToAsync(0, 0, Motion.StandardMs, Motion.EaseOut));
    }

    /// <summary>Out the way it came in, faster.</summary>
    private async Task HideRefinementsAsync()
    {
        _shownQuadrant = EmotionQuadrant.None;
        RefinePanel.CancelAnimations();

        await Task.WhenAll(
            RefinePanel.FadeToAsync(0, Motion.FastMs, Motion.EaseOut),
            RefinePanel.TranslateToAsync(0, Motion.Offset(-6), Motion.FastMs, Motion.EaseOut));

        // Re-shown while fading out: the show animation owns the panel now.
        if (_viewModel?.HasRefinements == true) return;

        RefinePanel.IsVisible = false;
        RefinePanel.TranslationY = 0;
    }

    /// <summary>
    /// Purpose: state indication. A short settle says "these are different chips" without the row
    /// leaving and re-entering, which would make every quadrant change feel like a navigation.
    /// </summary>
    private async Task SettleRefinementsAsync()
    {
        RefinePanel.CancelAnimations();
        RefinePanel.Opacity = 0.4;
        RefinePanel.TranslationY = Motion.Offset(-4);

        await Task.WhenAll(
            RefinePanel.FadeToAsync(1, Motion.FastMs, Motion.EaseOut),
            RefinePanel.TranslateToAsync(0, 0, Motion.FastMs, Motion.EaseOut));
    }
}
