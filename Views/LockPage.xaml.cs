using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class LockPage : ContentPage
{
    public LockViewModel ViewModel { get; }

    public LockPage(LockViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Diarion.Services.PageLoadGuard.RunAsync(this, () => ViewModel.OnAppearingAsync());
    }
}
