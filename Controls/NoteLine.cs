using System;
using System.ComponentModel;
using Diarion.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// A <see cref="RichTextBlock"/> already wired to a <see cref="NoteBlockViewModel"/>: every line of a
/// note is one of these, and the templates only decide what sits beside it and how big the font is.
/// </summary>
/// <remarks>
/// The bindings live here rather than in eight XAML templates so that they are written once, typed,
/// and compiled. Focus is reported to the block directly — the block is what the formatting bar asks
/// "which line" — and the placeholder is coloured in only on the one line of an empty note.
/// </remarks>
public class NoteLine : RichTextBlock
{
    public static readonly BindableProperty IsPlaceholderShownProperty =
        BindableProperty.Create(nameof(IsPlaceholderShown), typeof(bool), typeof(NoteLine), false,
            propertyChanged: (b, _, _) => ((NoteLine)b).UpdatePlaceholderColor());

    /// <summary>The colour the placeholder has when it is shown at all; see <see cref="IsPlaceholderShown"/>.</summary>
    public static readonly BindableProperty ActivePlaceholderColorProperty =
        BindableProperty.Create(nameof(ActivePlaceholderColor), typeof(Color), typeof(NoteLine), Colors.Gray,
            propertyChanged: (b, _, _) => ((NoteLine)b).UpdatePlaceholderColor());

    public NoteLine()
    {
        this.SetBinding(BodyProperty, static (NoteBlockViewModel vm) => vm.Body, BindingMode.TwoWay);
        this.SetBinding(TypingStyleProperty, static (NoteBlockViewModel vm) => vm.TypingStyle, BindingMode.TwoWay);
        this.SetBinding(SelectionStartProperty, static (NoteBlockViewModel vm) => vm.SelectionStart, BindingMode.TwoWay);
        this.SetBinding(SelectionLengthProperty, static (NoteBlockViewModel vm) => vm.SelectionLength, BindingMode.TwoWay);
        this.SetBinding(IsFocusRequestedProperty, static (NoteBlockViewModel vm) => vm.IsFocusRequested, BindingMode.TwoWay);
        this.SetBinding(CaretProperty, static (NoteBlockViewModel vm) => vm.Caret);
        this.SetBinding(IsStruckProperty, static (NoteBlockViewModel vm) => vm.IsStruck);
        this.SetBinding(IsPlaceholderShownProperty, static (NoteBlockViewModel vm) => vm.ShowsPlaceholder);

        Focused += (_, _) => Block?.HoldEditCommand.Execute(null);
        Unfocused += (_, _) => Block?.EndEditCommand.Execute(null);

        UpdatePlaceholderColor();
    }

    public bool IsPlaceholderShown
    {
        get => (bool)GetValue(IsPlaceholderShownProperty);
        set => SetValue(IsPlaceholderShownProperty, value);
    }

    public Color ActivePlaceholderColor
    {
        get => (Color)GetValue(ActivePlaceholderColorProperty);
        set => SetValue(ActivePlaceholderColorProperty, value);
    }

    private NoteBlockViewModel? Block => BindingContext as NoteBlockViewModel;

    private void UpdatePlaceholderColor()
        => PlaceholderColor = IsPlaceholderShown ? ActivePlaceholderColor : Colors.Transparent;
}
