using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class PromptHistoryPage : ContentPage
{
    private readonly PromptHistoryViewModel _viewModel;

    public PromptHistoryPage(PromptHistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    // Reloaded on every appearance so an answer edited on the day screen shows the change on the way back.
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
