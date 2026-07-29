using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class PromptLibraryPage : ContentPage
{
    private readonly PromptLibraryViewModel _viewModel;

    public PromptLibraryPage(PromptLibraryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    // Reloaded on every appearance so returning from the editor shows the change.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
