using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class PinSetupPage : ContentPage
{
    public PinSetupViewModel ViewModel { get; }

    public PinSetupPage(PinSetupViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;

        ViewModel.Completed = () => _ = Shell.Current.GoToAsync("..");
        ViewModel.Cancelled = () => _ = Shell.Current.GoToAsync("..");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ViewModel.Initialize();
    }
}
