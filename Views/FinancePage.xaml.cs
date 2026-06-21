using System;
using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class FinancePage : ContentPage
{
    private readonly FinanceViewModel _viewModel;

    public FinancePage(FinanceViewModel viewModel)
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
