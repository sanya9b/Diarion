using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class StatisticsPage : ContentPage
{
    private readonly StatisticsViewModel _viewModel;

    public StatisticsPage(StatisticsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Before loading: the cycle tab appears and disappears with the setting, and selecting a tab
        // that has just been removed would leave the page showing nothing.
        await _viewModel.RefreshCycleTabAvailabilityAsync();
        await _viewModel.LoadStatisticsAsync();
    }
}
