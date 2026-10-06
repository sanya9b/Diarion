using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models.Markdown;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// One editable line of a note. The marker is not in the text — it is drawn beside it as a bullet, a
/// number or a tick box — and neither is the emphasis: <see cref="Body"/> holds bold as bold, not as
/// a pair of stars, so the user never sees the markdown that is actually stored.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> is deliberately read-only. A block that changes kind is replaced with a new
/// instance rather than mutated, because the template a block is drawn with is chosen once when the
/// item is realised: a bullet that quietly became a tick box would keep the bullet's template and
/// the tick box would never appear.
/// </remarks>
public partial class NoteBlockViewModel : ObservableObject
{
    private readonly Action<NoteBlockViewModel>? _onEdited;
    private readonly Action<NoteBlockViewModel>? _onFocused;
    private bool _silent;

    public NoteBlockViewModel(
        MarkdownBlock block,
        Action<NoteBlockViewModel>? onEdited = null,
        Action<NoteBlockViewModel>? onFocused = null)
    {
        Kind = block.Kind;
        Indent = block.Indent;
        _body = StyledText.Parse(block.Text);
        _isChecked = block.IsChecked;
        _number = block.Number;
        _onEdited = onEdited;
        _onFocused = onFocused;
    }

    public MarkdownBlockKind Kind { get; }

    /// <summary>Leading whitespace carried through from the stored text; see <see cref="MarkdownBlock.Indent"/>.</summary>
    public string Indent { get; }

    /// <summary>
    /// The line as the field holds it: characters and their look, no markup. The field writes here
    /// on every keystroke and redraws from here whenever the editor reshapes the line.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private StyledText _body = StyledText.Empty;

    /// <summary>
    /// The look the next character typed at the caret will have. Two-way with the field: the field
    /// reports what the caret inherited from the character before it, and the formatting bar flips
    /// bits in it when nothing is selected — press bold, type, and the word arrives bold.
    /// </summary>
    [ObservableProperty]
    private InlineStyle _typingStyle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStruck))]
    private bool _isChecked;

    /// <summary>True while the caret is in this line.</summary>
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberLabel))]
    private int _number;

    /// <summary>Set to true to ask the view to put the keyboard in this block; the view clears it.</summary>
    [ObservableProperty]
    private bool _isFocusRequested;

    /// <summary>Where the caret goes when focus arrives.</summary>
    [ObservableProperty]
    private int _caret;

    /// <summary>
    /// Where the caret is now, reported by the field as the user moves it. The opposite direction to
    /// <see cref="Caret"/>, and the reason the formatting bar can act on the selection rather than
    /// on the whole line.
    /// </summary>
    [ObservableProperty]
    private int _selectionStart;

    /// <summary>How much text is selected, if any; zero is a plain caret.</summary>
    [ObservableProperty]
    private int _selectionLength;

    /// <summary>
    /// True only for the single empty line of an empty note. Set by the editor rather than worked out
    /// from <see cref="Text"/>, because a blank line in the middle of a note is also an empty block and
    /// must not invite the user to write something in it.
    /// </summary>
    [ObservableProperty]
    private bool _showsPlaceholder;

    public string NumberLabel => $"{Number}.";

    /// <summary>
    /// The characters of the line with the look forgotten. Setting it replaces the line with plain
    /// text — any markup typed in it is read and turned into style, exactly as if it had been typed
    /// into the field.
    /// </summary>
    public string Text
    {
        get => Body.Text;
        set
        {
            if (value == Body.Text) return;
            Body = StyledText.Plain(value);
        }
    }

    /// <summary>A ticked item is drawn crossed out and dimmed, whatever its own styles say.</summary>
    public bool IsStruck => Kind == MarkdownBlockKind.Checklist && IsChecked;

    public MarkdownBlock ToBlock() => new()
    {
        Kind = Kind,
        Text = MarkdownParser.ComposeInline(Body),
        IsChecked = IsChecked,
        Number = Number,
        Indent = Indent
    };

    /// <summary>
    /// Changes the line without telling the editor about it. Used when the editor itself is the one
    /// rewriting the block — splitting it, or joining it with the line above — so that reshaping the
    /// list does not immediately re-enter the code that is reshaping it.
    /// </summary>
    public void SetBodySilently(StyledText value)
    {
        _silent = true;
        try
        {
            Body = value;
        }
        finally
        {
            _silent = false;
        }
    }

    public void RequestFocus(int caret)
    {
        Caret = caret;
        IsFocusRequested = true;
    }

    [RelayCommand]
    private void ToggleCheck() => IsChecked = !IsChecked;

    /// <summary>The field took focus: this is the line the formatting bar now acts on.</summary>
    [RelayCommand]
    private void HoldEdit()
    {
        IsEditing = true;
        _onFocused?.Invoke(this);
    }

    /// <summary>Focus has left the line.</summary>
    [RelayCommand]
    private void EndEdit() => IsEditing = false;

    partial void OnBodyChanged(StyledText value)
    {
        if (_silent) return;

        // The user typed the markup by hand — "**молоко**" — or pasted a line that carries it. It
        // becomes style the moment it closes, the same way "- " becomes a bullet: the stars were
        // never meant to be seen. The caret sat after the closing marker, so it moves back by exactly
        // the characters that vanished.
        if (value.ContainsMarkup)
        {
            var expanded = value.ExpandMarkup();
            var removed = value.Length - expanded.Length;
            SetBodySilently(expanded);
            RequestFocus(Math.Clamp(SelectionStart - removed, 0, expanded.Length));
        }

        _onEdited?.Invoke(this);
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_silent) _onEdited?.Invoke(this);
    }
}
