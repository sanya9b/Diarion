using Diarion.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

namespace Diarion.Controls;

/// <summary>
/// Resolves its own view model, like <see cref="AccessBanner"/>: the path is about the diary as a
/// whole, not about the day on screen. No motion of its own beyond the strip's fill and the press
/// feedback — the row lives on the daily surface, where the motion gate allows nothing more.
/// </summary>
public partial class InsightPathCard : ContentView
{
    private readonly InsightPathViewModel? _viewModel;

    public InsightPathCard()
    {
        InitializeComponent();
        _viewModel = IPlatformApplication.Current?.Services.GetService<InsightPathViewModel>();
        BindingContext = _viewModel;

        if (_viewModel is not null)
            Loaded += (_, _) => _ = _viewModel.RefreshAsync();
    }

    /// <summary>For the page's OnAppearing: a day may have been written on another screen while we were away.</summary>
    public void Refresh()
    {
        if (_viewModel is not null)
            _ = _viewModel.RefreshAsync();
    }
}
