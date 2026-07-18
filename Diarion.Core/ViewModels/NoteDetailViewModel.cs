using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using LiteDB;

namespace Diarion.ViewModels;

[QueryProperty(nameof(NoteId), "NoteId")]
public partial class NoteDetailViewModel : BaseViewModel
{
    private readonly INoteService _noteService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string? _noteId;

    [ObservableProperty]
    private Note _currentNote = new();

    [ObservableProperty]
    private string _noteContent = string.Empty;

    private readonly Helpers.AsyncDebouncer _autoSaveDebouncer = new(TimeSpan.FromSeconds(1));

    public NoteDetailViewModel(
        INoteService noteService, 
        INavigationService navigationService,
        IDialogService dialogService)
    {
        _noteService = noteService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        Title = "Note";
    }

    partial void OnNoteIdChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = LoadNoteAsync(value);
        }
    }

    private async Task LoadNoteAsync(string id)
    {
        IsBusy = true;
        try
        {
            var note = await _noteService.GetNoteAsync(id);
            if (note != null)
            {
                CurrentNote = note;
#pragma warning disable MVVMTK0034 // Direct reference to observable backing field
                _noteContent = note.Content; // Set backing field to avoid triggering OnNoteContentChanged immediately
#pragma warning restore MVVMTK0034
                OnPropertyChanged(nameof(NoteContent));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading note: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnNoteContentChanged(string value)
    {
        CurrentNote.Content = value;
        _autoSaveDebouncer.Debounce(async () =>
        {
            await SaveNoteInternalAsync();
        });
    }

    private async Task SaveNoteInternalAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentNote.Content))
        {
            return;
        }

        // Auto generate title from content
        var lines = CurrentNote.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0)
        {
            var firstLine = lines[0].Trim();
            CurrentNote.Title = firstLine.Length > 40 ? firstLine.Substring(0, 40) + "..." : firstLine;
        }
        else
        {
            CurrentNote.Title = "Untitled";
        }

        try
        {
            await _noteService.SaveNoteAsync(CurrentNote);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error auto-saving note: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task FlushSaveAsync()
    {
        CurrentNote.Content = NoteContent;
        if (!string.IsNullOrWhiteSpace(CurrentNote.Content))
        {
            IsBusy = true;
            try
            {
                await SaveNoteInternalAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        // Flush debouncer and save immediately, then navigate back
        await FlushSaveAsync();
        await _navigationService.NavigateBackAsync();
    }

    [RelayCommand]
    private async Task DeleteNoteAsync()
    {
        if (CurrentNote.Id != ObjectId.Empty)
        {
            bool confirm = await _dialogService.ShowConfirmationAsync(
                "Delete Note", 
                "Are you sure you want to delete this note?", 
                "Yes", "No");
                
            if (confirm)
            {
                IsBusy = true;
                try
                {
                    await _noteService.DeleteNoteAsync(CurrentNote.Id.ToString());
                    await _navigationService.NavigateBackAsync();
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }
        else
        {
            await _navigationService.NavigateBackAsync();
        }
    }
}
