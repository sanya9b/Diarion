using System;
using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class WishlistPage : ContentPage
{
    private readonly WishlistViewModel _viewModel;

    public WishlistPage(WishlistViewModel viewModel)
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
        await Shell.Current.Navigation.PopModalAsync();
    }
}
