using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels;

/// <summary>
/// The note field at the foot of the home screen. What is written there is not part of the day: it goes
/// to Notes, into the inbox, the same way the capture bar on the Notes page files it, and the field
/// empties for the next thought.
/// </summary>
public partial class QuickNoteViewModel : ObservableObject
{
    private readonly INoteService _noteService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private string _text = string.Empty;

    /// <summary>True right after a send, until the next keystroke: the one sign the note went somewhere.</summary>
    [ObservableProperty]
    private bool _justSaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool _isSending;

    public QuickNoteViewModel(INoteService noteService)
    {
        _noteService = noteService;
    }

    public bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(Text);

    partial void OnTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(value)) JustSaved = false;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Text?.Trim();
        if (IsSending || string.IsNullOrEmpty(text)) return;

        IsSending = true;
        try
        {
            if (!await WriteGate.TryAsync(() => _noteService.SaveNoteAsync(new Note { Content = text, IsInInbox = true }))) return;

            Text = string.Empty;
            JustSaved = true;
        }
        finally
        {
            IsSending = false;
        }
    }
}
