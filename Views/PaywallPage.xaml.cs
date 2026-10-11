using Diarion.ViewModels;
using Microsoft.Maui.Controls;

namespace Diarion.Views;

public partial class PaywallPage : ContentPage
{
    private bool _loaded;

    public PaywallViewModel ViewModel { get; }

    public PaywallPage(PaywallViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>Android's back button is "Not now", never a way to leave the sheet half-closed.</summary>
    protected override bool OnBackButtonPressed()
    {
        ViewModel.LaterCommand.Execute(null);
        return true;
    }
}
