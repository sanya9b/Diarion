using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;

namespace Diarion.ViewModels;

public enum NoteFilterKind { All, Inbox, Tag }

/// <summary>A chip in the notes filter row: "All", "Inbox" (when any inbox notes exist), or a tag.</summary>
public partial class NoteFilterChip : ObservableObject
{
    public NoteFilterKind Kind { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class NotesViewModel : BaseViewModel
{
    private readonly INoteService _noteService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<Note> Notes { get; } = new();
    public ObservableCollection<NoteFilterChip> Filters { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Text of the bottom quick-capture bar.</summary>
    [ObservableProperty]
    private string _quickCaptureText = string.Empty;

    /// <summary>Hide the filter row when there is nothing to filter by (only the "All" chip).</summary>
    [ObservableProperty]
    private bool _showFilters;

    private NoteFilterKind _currentFilterKind = NoteFilterKind.All;
    private string _currentTag = string.Empty;

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
        await RebuildFiltersAsync();
        await LoadNotesAsync();
    }

    private async Task RebuildFiltersAsync()
    {
        var all = await _noteService.GetAllNotesAsync() ?? new System.Collections.Generic.List<Note>();
        var tags = all.Where(n => n.Tags != null)
            .SelectMany(n => n.Tags)
            .Distinct()
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool hasInbox = all.Any(n => n.IsInInbox);

        // A tag filter is dropped if its tag no longer exists; fall back to All.
        if (_currentFilterKind == NoteFilterKind.Tag && !tags.Contains(_currentTag))
        {
            _currentFilterKind = NoteFilterKind.All;
            _currentTag = string.Empty;
        }
        if (_currentFilterKind == NoteFilterKind.Inbox && !hasInbox)
        {
            _currentFilterKind = NoteFilterKind.All;
        }

        Filters.Clear();
        Filters.Add(new NoteFilterChip
        {
            Kind = NoteFilterKind.All,
            DisplayName = AppResources.NotesFilterAll,
            IsSelected = _currentFilterKind == NoteFilterKind.All
        });
        if (hasInbox)
        {
            Filters.Add(new NoteFilterChip
            {
                Kind = NoteFilterKind.Inbox,
                DisplayName = AppResources.NotesFilterInbox,
                IsSelected = _currentFilterKind == NoteFilterKind.Inbox
            });
        }
        foreach (var tag in tags)
        {
            Filters.Add(new NoteFilterChip
            {
                Kind = NoteFilterKind.Tag,
                Tag = tag,
                DisplayName = "#" + tag,
                IsSelected = _currentFilterKind == NoteFilterKind.Tag && _currentTag == tag
            });
        }

        ShowFilters = Filters.Count > 1;
    }

    [RelayCommand]
    private async Task SelectFilterAsync(NoteFilterChip chip)
    {
        if (chip == null) return;

        _currentFilterKind = chip.Kind;
        _currentTag = chip.Tag;

        foreach (var f in Filters)
        {
            f.IsSelected = ReferenceEquals(f, chip);
        }

        await LoadNotesAsync();
    }

    [RelayCommand]
    private async Task LoadNotesAsync()
    {
        IsBusy = true;
        try
        {
            var results = await _noteService.SearchNotesAsync(SearchQuery);

            var filtered = _currentFilterKind switch
            {
                NoteFilterKind.Inbox => results.Where(n => n.IsInInbox),
                NoteFilterKind.Tag => results.Where(n => n.Tags != null && n.Tags.Contains(_currentTag)),
                _ => results.AsEnumerable()
            };

            Notes.Clear();
            foreach (var note in filtered)
            {
                Notes.Add(note);
            }
        }
        catch (Exception ex)
        {
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
    private async Task QuickCaptureAsync()
    {
        var text = QuickCaptureText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // Fast path: save straight to the inbox without opening the editor.
        await _noteService.SaveNoteAsync(new Note { Content = text, IsInInbox = true });
        QuickCaptureText = string.Empty;

        await RebuildFiltersAsync(); // the "Inbox" chip appears once the first inbox note exists
        await LoadNotesAsync();
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
            AppResources.DeleteNoteConfirmTitle,
            AppResources.DeleteNoteConfirmMessage,
            AppResources.DeleteConfirmYes,
            AppResources.DeleteConfirmNo);

        if (confirm)
        {
            await _noteService.DeleteNoteAsync(note.Id.ToString());
            Notes.Remove(note);
            await RebuildFiltersAsync(); // a tag may no longer exist after deletion
        }
    }
}
