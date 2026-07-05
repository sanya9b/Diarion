using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class NotesViewModel : BaseViewModel
{
    private readonly INoteService _noteService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<Note> Notes { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public NotesViewModel(INoteService noteService, INavigationService navigationService, IDialogService dialogService)
    {
        _noteService = noteService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = "Notes";
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        await LoadNotesAsync();
    }

    [RelayCommand]
    private async Task LoadNotesAsync()
    {
        IsBusy = true;
        try
        {
            var notes = await _noteService.SearchNotesAsync(SearchQuery);
            Notes.Clear();
            foreach (var note in notes)
            {
                Notes.Add(note);
            }
        }
        catch (Exception ex)
        {
            // Handle error (maybe via a dialog service if available)
            Console.WriteLine($"Error loading notes: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PerformSearchAsync()
    {
        await LoadNotesAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Simple search on text change, could be debounced
        _ = PerformSearchAsync();
    }

    [RelayCommand]
    private async Task CreateNewNoteAsync()
    {
        await _navigationService.NavigateToAsync("NoteDetail");
    }

    [RelayCommand]
    private async Task OpenNoteAsync(Note note)
    {
        if (note == null) return;
        
        await _navigationService.NavigateToAsync("NoteDetail", new System.Collections.Generic.Dictionary<string, object>
        {
            { "NoteId", note.Id.ToString() }
        });
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(Note note)
    {
        if (note == null) return;

        bool confirm = await _dialogService.ShowConfirmationAsync(
            "Delete Note", 
            "Are you sure you want to delete this note?", 
            "Yes", "No");

        if (confirm)
        {
            await _noteService.DeleteNoteAsync(note.Id.ToString());
            Notes.Remove(note);
        }
    }
}
