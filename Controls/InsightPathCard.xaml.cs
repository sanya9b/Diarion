using System.Threading.Tasks;
using Diarion.Resources.Styles;
using Diarion.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

namespace Diarion.Controls;

/// <summary>
/// Resolves its own view model, like <see cref="AccessBanner"/>: the path is about the diary as a
/// whole, not about the day on screen. The one piece of motion here is the milestone block appearing
/// when a milestone is reached while the card is on screen — the celebration the spec budgets for
/// (R2.4), once per milestone, never on load.
/// </summary>
public partial class InsightPathCard : ContentView
{
    private readonly InsightPathViewModel? _viewModel;

    public InsightPathCard()
    {
        InitializeComponent();
        _viewModel = IPlatformApplication.Current?.Services.GetService<InsightPathViewModel>();
        BindingContext = _viewModel;

        if (_viewModel is null)
            return;

        _viewModel.MilestoneReached += milestone => _ = CelebrateAsync();
        Loaded += (_, _) => _ = _viewModel.RefreshAsync();
    }

    /// <summary>For the page's OnAppearing: a day may have been written while we were away.</summary>
    public void Refresh()
    {
        if (_viewModel is not null)
            _ = _viewModel.RefreshAsync();
    }

    private async Task CelebrateAsync()
    {
        MilestoneBlock.Opacity = 0;
        MilestoneBlock.Scale = Motion.Scale(0.96);
        await Task.WhenAll(
            MilestoneBlock.FadeToAsync(1, Motion.StandardMs, Motion.EaseOut),
            MilestoneBlock.ScaleToAsync(1, Motion.StandardMs, Motion.EaseOut));
    }
}
