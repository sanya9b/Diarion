using System;
using Microsoft.Maui.Controls;
using Diarion.Models;
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

    /// <summary>The swiped note, handed to the same command that confirms and deletes.</summary>
    private void OnDeleteSwipeInvoked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: Note note } && BindingContext is NotesViewModel vm)
            vm.DeleteNoteCommand.Execute(note);
    }
}
