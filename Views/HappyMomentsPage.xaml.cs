using Diarion.ViewModels;

namespace Diarion.Views;

public partial class HappyMomentsPage : ContentPage
{
    private readonly HappyMomentsViewModel _viewModel;

    public HappyMomentsPage(HappyMomentsViewModel viewModel)
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