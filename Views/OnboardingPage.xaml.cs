using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class OnboardingPage : ContentPage
{
    public OnboardingViewModel ViewModel { get; }

    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;
    }
}
