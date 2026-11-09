using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class RecoveryPage : ContentPage
{
    public RecoveryPage(RecoveryViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;
    }

    public RecoveryViewModel ViewModel { get; }

    /// <summary>Android's back button is "start fresh" only by a tap, never by accident.</summary>
    protected override bool OnBackButtonPressed() => true;
}
