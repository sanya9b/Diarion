using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class SnapshotPreviewPage : ContentPage
{
    private readonly SnapshotPreviewViewModel _viewModel;

    public SnapshotPreviewPage(SnapshotPreviewViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    // Rebuilt on every appearance rather than cached: the whole claim of this screen is that it shows
    // the current payload, and an entry edited since the last visit would make that false.
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
