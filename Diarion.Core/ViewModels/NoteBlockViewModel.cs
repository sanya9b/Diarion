using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models.Markdown;
using Diarion.Services;

namespace Diarion.ViewModels;

/// <summary>
/// One editable line of a note. The marker is not in <see cref="Text"/> — it is drawn beside it as a
/// bullet, a number or a tick box — so the user never sees the markdown that is actually stored.
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
        _text = block.Text;
        _isChecked = block.IsChecked;
        _number = block.Number;
        _onEdited = onEdited;
        _onFocused = onFocused;
    }

    public MarkdownBlockKind Kind { get; }

    /// <summary>Leading whitespace carried through from the stored text; see <see cref="MarkdownBlock.Indent"/>.</summary>
    public string Indent { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Spans))]
    [NotifyPropertyChangedFor(nameof(ShowsFormattedText))]
    [NotifyPropertyChangedFor(nameof(ShowsRawText))]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStruck))]
    [NotifyPropertyChangedFor(nameof(ShowsFormattedText))]
    [NotifyPropertyChangedFor(nameof(ShowsRawText))]
    private bool _isChecked;

    /// <summary>
    /// True while the caret is in this line. The markup shows itself only here, and only for as long
    /// as it takes to edit it — the line you are not typing in is the line you see formatted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFormattedText))]
    [NotifyPropertyChangedFor(nameof(ShowsRawText))]
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
    /// <see cref="Caret"/>, and the reason the formatting bar can act on the word you are standing in
    /// rather than on the whole line.
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

    /// <summary>The line cut into the stretches that are drawn differently — bold, code, a link title.</summary>
    public IReadOnlyList<InlineSpan> Spans => MarkdownParser.ParseInline(Text);

    /// <summary>A ticked item is crossed out, which only a label can do.</summary>
    public bool IsStruck => Kind == MarkdownBlockKind.Checklist && IsChecked;

    /// <summary>
    /// Whether this line is currently drawn as formatted text rather than as an input field.
    /// </summary>
    /// <remarks>
    /// A MAUI input field cannot format part of its own text, so a line that has something to show —
    /// emphasis, or a tick that should cross it out — is drawn as a label until it is tapped. Lines
    /// with nothing to reveal stay input fields the whole time, which matters: that is what keeps the
    /// caret landing exactly where the finger did, and it is nearly every line in a note.
    /// </remarks>
    public bool ShowsFormattedText
        => !IsEditing
            && !string.IsNullOrEmpty(Text)
            && (IsStruck || MarkdownParser.HasInlineMarkup(Text));

    public bool ShowsRawText => !ShowsFormattedText;

    public MarkdownBlock ToBlock() => new()
    {
        Kind = Kind,
        Text = Text ?? string.Empty,
        IsChecked = IsChecked,
        Number = Number,
        Indent = Indent
    };

    /// <summary>
    /// Changes the text without telling the editor about it. Used when the editor itself is the one
    /// rewriting the block — splitting it, or joining it with the line above — so that reshaping the
    /// list does not immediately re-enter the code that is reshaping it.
    /// </summary>
    public void SetTextSilently(string value)
    {
        _silent = true;
        try
        {
            Text = value;
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

    /// <summary>Tapping a formatted line: show it as it is written, with the caret in it.</summary>
    /// <remarks>
    /// The caret goes to the end because a label cannot say which character was tapped. That is the
    /// price of showing the line formatted, and the reason lines with no markup are never labels.
    /// </remarks>
    [RelayCommand]
    private void BeginEdit()
    {
        IsEditing = true;
        _onFocused?.Invoke(this);
        RequestFocus((Text ?? string.Empty).Length);
    }

    /// <summary>The field itself took focus — the user tapped straight into it, caret and all.</summary>
    [RelayCommand]
    private void HoldEdit()
    {
        IsEditing = true;
        _onFocused?.Invoke(this);
    }

    /// <summary>Focus has left the line: whatever markup it holds goes back to being drawn.</summary>
    [RelayCommand]
    private void EndEdit() => IsEditing = false;

    partial void OnTextChanged(string value)
    {
        if (!_silent) _onEdited?.Invoke(this);
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_silent) _onEdited?.Invoke(this);
    }
}
