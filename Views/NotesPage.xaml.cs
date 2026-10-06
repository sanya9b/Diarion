using System;
using Microsoft.Maui.Controls;
using Diarion.ViewModels;

namespace Diarion.Views;

public partial class NotesPage : ContentPage
{
    public NotesPage(NotesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is NotesViewModel vm)
        {
            vm.InitializeCommand.Execute(null);
        }
    }

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.Navigation.PopModalAsync();
    }
}
