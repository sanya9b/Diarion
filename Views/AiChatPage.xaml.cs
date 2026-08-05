using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class AiChatPage : ContentPage
{
    private readonly AiChatViewModel _viewModel;

    public AiChatPage(AiChatViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshAvailabilityAsync();
    }
}
