using Diarion.ViewModels;

namespace Diarion.Views;

public partial class HabitTrackerPage : ContentPage
{
    private readonly HabitTrackerViewModel _viewModel;

    public HabitTrackerPage(HabitTrackerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}