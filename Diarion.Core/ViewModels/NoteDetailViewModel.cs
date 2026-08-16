using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Models.Markdown;
using Diarion.Resources.Localization;
using Diarion.Services;
using LiteDB;

namespace Diarion.ViewModels;

/// <summary>An outgoing <c>[[link]]</c> shown under a note: its display title and whether it resolves to
/// an existing note (missing targets are created on tap).</summary>
public class NoteLinkItem
{
    public string Title { get; set; } = string.Empty;
    public bool Exists { get; set; }
}

[QueryProperty(nameof(NoteId), "NoteId")]
public partial class NoteDetailViewModel : BaseViewModel
{
    private readonly INoteService _noteService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IKeyboardInsetService _keyboard;
    private bool _watchingKeyboard;

    [ObservableProperty]
    private string? _noteId;

    [ObservableProperty]
    private Note _currentNote = new();

    /// <summary>
    /// The note as it is stored: markdown, and the only thing that is ever saved. The blocks below
    /// are a view of this string, rebuilt when the note is opened and written back on every edit.
    /// </summary>
    [ObservableProperty]
    private string _noteContent = string.Empty;

    private readonly NoteBlockEditor _body = new();

    /// <summary>The body as the screen draws it — one block per marked line, prose in runs.</summary>
    public ObservableCollection<NoteBlockViewModel> Blocks => _body.Blocks;

    /// <summary>Tags (without '#') parsed from the body, shown as chips.</summary>
    public ObservableCollection<string> Tags { get; } = new();

    /// <summary>Outgoing <c>[[links]]</c> in this note.</summary>
    public ObservableCollection<NoteLinkItem> OutgoingLinks { get; } = new();

    /// <summary>Other notes that link to this one.</summary>
    public ObservableCollection<Note> Backlinks { get; } = new();

    [ObservableProperty]
    private bool _hasTags;

    [ObservableProperty]
    private bool _hasOutgoingLinks;

    [ObservableProperty]
    private bool _hasBacklinks;

    private readonly Helpers.AsyncDebouncer _autoSaveDebouncer = new(TimeSpan.FromSeconds(1));

    /// <summary>
    /// How far the formatting bar sits off the bottom of the page: nothing until the keyboard covers
    /// it, and the height of the keyboard once it does. See <see cref="IKeyboardInsetService"/> for
    /// why only one platform ever answers with anything.
    /// </summary>
    [ObservableProperty]
    private double _keyboardInset;

    public NoteDetailViewModel(
        INoteService noteService,
        INavigationService navigationService,
        IDialogService dialogService,
        IKeyboardInsetService keyboard)
    {
        _noteService = noteService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _keyboard = keyboard;
        _body.Changed += OnBodyChanged;
        Title = "Note";
    }

    private void OnBodyChanged(object? sender, EventArgs e) => NoteContent = _body.Compose();

    /// <summary>Tapping the empty space under a note puts the caret at its end, as in any editor.</summary>
    [RelayCommand]
    private void FocusEnd() => _body.FocusEnd();

    /// <summary>
    /// Starts listening for the keyboard, and stops. Tied to the page being on screen because the
    /// service outlives it: a note left subscribed is a note that is never collected.
    /// </summary>
    [RelayCommand]
    private void WatchKeyboard()
    {
        if (_watchingKeyboard) return;

        _keyboard.OverlapChanged += OnKeyboardMoved;
        _watchingKeyboard = true;
        KeyboardInset = _keyboard.Overlap;
    }

    [RelayCommand]
    private void ForgetKeyboard()
    {
        if (!_watchingKeyboard) return;

        _keyboard.OverlapChanged -= OnKeyboardMoved;
        _watchingKeyboard = false;
        KeyboardInset = 0;
    }

    private void OnKeyboardMoved(object? sender, EventArgs e) => KeyboardInset = _keyboard.Overlap;

    /// <summary>
    /// The formatting bar. Eight buttons over the same operations the markdown symbols perform, for
    /// the reader who does not know the symbols — typing "- " keeps working and reaches the same code.
    /// </summary>
    [RelayCommand]
    private void ToggleChecklist() => _body.ToggleKind(MarkdownBlockKind.Checklist);

    [RelayCommand]
    private void ToggleBullet() => _body.ToggleKind(MarkdownBlockKind.Bullet);

    [RelayCommand]
    private void ToggleNumbered() => _body.ToggleKind(MarkdownBlockKind.Numbered);

    /// <summary>One button, four sizes: H1 → H2 → H3 → plain text.</summary>
    [RelayCommand]
    private void CycleHeading() => _body.CycleHeading();

    [RelayCommand]
    private void ToggleQuote() => _body.ToggleKind(MarkdownBlockKind.Quote);

    [RelayCommand]
    private void ToggleBold() => _body.ToggleInline("**");

    [RelayCommand]
    private void ToggleItalic() => _body.ToggleInline("*");

    [RelayCommand]
    private void ToggleStrikethrough() => _body.ToggleInline("~~");

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
                _body.Load(note.Content);
                await RefreshMetadataAsync();
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

        // The title is the first line that says something, as the user sees it: a note that opens
        // with "# Покупки" is called "Покупки", not "# Покупки" — otherwise the markup would leak
        // into the notes list, into search and into every [[link]] pointing at this note.
        var firstLine = MarkdownParser.FirstPlainLine(CurrentNote.Content);
        CurrentNote.Title = firstLine.Length switch
        {
            0 => AppResources.NoteUntitled,
            > 40 => firstLine.Substring(0, 40) + "...",
            _ => firstLine
        };

        try
        {
            await _noteService.SaveNoteAsync(CurrentNote);
            await RefreshMetadataAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error auto-saving note: {ex.Message}");
        }
    }

    private async Task RefreshMetadataAsync()
    {
        Tags.Clear();
        foreach (var tag in NoteParser.ExtractTags(CurrentNote.Content))
        {
            Tags.Add(tag);
        }
        HasTags = Tags.Count > 0;

        OutgoingLinks.Clear();
        foreach (var display in NoteParser.ExtractLinkDisplayTitles(CurrentNote.Content))
        {
            var target = await _noteService.GetNoteByTitleAsync(display);
            OutgoingLinks.Add(new NoteLinkItem { Title = display, Exists = target != null });
        }
        HasOutgoingLinks = OutgoingLinks.Count > 0;

        Backlinks.Clear();
        if (!string.IsNullOrWhiteSpace(CurrentNote.Title))
        {
            var backs = await _noteService.GetBacklinksAsync(CurrentNote.Title, CurrentNote.Id.ToString());
            foreach (var b in backs)
            {
                Backlinks.Add(b);
            }
        }
        HasBacklinks = Backlinks.Count > 0;
    }

    [RelayCommand]
    private async Task OpenLinkedNoteAsync(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        await FlushSaveAsync(); // persist the current note before leaving

        var target = await _noteService.GetNoteByTitleAsync(title);
        string id;
        if (target == null)
        {
            // Obsidian-style: tapping a link to a missing note creates it.
            var newNote = new Note { Content = title };
            await _noteService.SaveNoteAsync(newNote);
            id = newNote.Id.ToString();
        }
        else
        {
            id = target.Id.ToString();
        }

        await _navigationService.NavigateToAsync("NoteDetail", new Dictionary<string, object> { { "NoteId", id } });
    }

    [RelayCommand]
    private async Task OpenBacklinkAsync(Note? note)
    {
        if (note == null) return;

        await FlushSaveAsync();
        await _navigationService.NavigateToAsync("NoteDetail", new Dictionary<string, object> { { "NoteId", note.Id.ToString() } });
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
                AppResources.DeleteNoteConfirmTitle,
                AppResources.DeleteNoteConfirmMessage,
                AppResources.DeleteConfirmYes,
                AppResources.DeleteConfirmNo);
                
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
