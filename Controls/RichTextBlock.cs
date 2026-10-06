using System;
using Diarion.Models.Markdown;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// One line of a note as a field that draws bold as bold. The stock <see cref="Editor"/> cannot style
/// part of its own text, so this is a view with its own handler on each platform — a
/// <c>UITextView</c>, an <c>EditText</c> with spans, a <c>RichEditBox</c> — that speaks
/// <see cref="StyledText"/> both ways and never shows a marker.
/// </summary>
/// <remarks>
/// <para>
/// The contract with the handler, in the order the handler must honour it: when the user types, the
/// handler first reports <see cref="SelectionStart"/> and <see cref="SelectionLength"/>, then
/// <see cref="Body"/>, then <see cref="TypingStyle"/>. The view model reads the selection while
/// handling the body change — to put the caret back after markup it just expanded — so the selection
/// has to be there first.
/// </para>
/// <para>
/// Every property the handler writes is two-way by default, so plain <c>{Binding}</c> in XAML is
/// enough. <see cref="IsFocusRequested"/> is the one exception in spirit: the view model raises it,
/// the control honours it and lowers it again, exactly like the old focus behaviour did.
/// </para>
/// </remarks>
public class RichTextBlock : View
{
    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(nameof(Body), typeof(StyledText), typeof(RichTextBlock), StyledText.Empty,
            BindingMode.TwoWay, coerceValue: (_, value) => value ?? StyledText.Empty);

    public static readonly BindableProperty TypingStyleProperty =
        BindableProperty.Create(nameof(TypingStyle), typeof(InlineStyle), typeof(RichTextBlock), InlineStyle.None,
            BindingMode.TwoWay);

    public static readonly BindableProperty SelectionStartProperty =
        BindableProperty.Create(nameof(SelectionStart), typeof(int), typeof(RichTextBlock), 0, BindingMode.TwoWay);

    public static readonly BindableProperty SelectionLengthProperty =
        BindableProperty.Create(nameof(SelectionLength), typeof(int), typeof(RichTextBlock), 0, BindingMode.TwoWay);

    public static readonly BindableProperty IsFocusRequestedProperty =
        BindableProperty.Create(nameof(IsFocusRequested), typeof(bool), typeof(RichTextBlock), false,
            BindingMode.TwoWay, propertyChanged: OnIsFocusRequestedChanged);

    public static readonly BindableProperty CaretProperty =
        BindableProperty.Create(nameof(Caret), typeof(int), typeof(RichTextBlock), 0);

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(RichTextBlock), 18d);

    public static readonly BindableProperty FontAttributesProperty =
        BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(RichTextBlock), FontAttributes.None);

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(RichTextBlock), Colors.White);

    /// <summary>What a ticked item is drawn in, and the colour of nothing else.</summary>
    public static readonly BindableProperty DimColorProperty =
        BindableProperty.Create(nameof(DimColor), typeof(Color), typeof(RichTextBlock), Colors.Gray);

    public static readonly BindableProperty CodeColorProperty =
        BindableProperty.Create(nameof(CodeColor), typeof(Color), typeof(RichTextBlock), Colors.Orange);

    public static readonly BindableProperty LinkColorProperty =
        BindableProperty.Create(nameof(LinkColor), typeof(Color), typeof(RichTextBlock), Colors.Coral);

    public static readonly BindableProperty IsStruckProperty =
        BindableProperty.Create(nameof(IsStruck), typeof(bool), typeof(RichTextBlock), false);

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(RichTextBlock), string.Empty);

    public static readonly BindableProperty PlaceholderColorProperty =
        BindableProperty.Create(nameof(PlaceholderColor), typeof(Color), typeof(RichTextBlock), Colors.Transparent);

    /// <summary>
    /// A view to ride on the keyboard. Only iOS has a place for one — the keyboard's accessory view,
    /// which the system lifts and drops with the keyboard itself — and only iOS needs it, being the one
    /// platform that slides the keyboard over the page instead of resizing the page. Ignored elsewhere.
    /// </summary>
    public static readonly BindableProperty InputAccessoryProperty =
        BindableProperty.Create(nameof(InputAccessory), typeof(View), typeof(RichTextBlock), null);

    /// <summary>The characters and their look. What the view model holds, and what the user sees.</summary>
    public StyledText Body
    {
        get => (StyledText)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>The look the next character typed at the caret will have.</summary>
    public InlineStyle TypingStyle
    {
        get => (InlineStyle)GetValue(TypingStyleProperty);
        set => SetValue(TypingStyleProperty, value);
    }

    public int SelectionStart
    {
        get => (int)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public int SelectionLength
    {
        get => (int)GetValue(SelectionLengthProperty);
        set => SetValue(SelectionLengthProperty, value);
    }

    /// <summary>Set to ask for the keyboard, with the caret at <see cref="Caret"/>; cleared once honoured.</summary>
    public bool IsFocusRequested
    {
        get => (bool)GetValue(IsFocusRequestedProperty);
        set => SetValue(IsFocusRequestedProperty, value);
    }

    public int Caret
    {
        get => (int)GetValue(CaretProperty);
        set => SetValue(CaretProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>The line's own weight and slant — a heading is bold throughout — under which the runs add theirs.</summary>
    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public Color DimColor
    {
        get => (Color)GetValue(DimColorProperty);
        set => SetValue(DimColorProperty, value);
    }

    public Color CodeColor
    {
        get => (Color)GetValue(CodeColorProperty);
        set => SetValue(CodeColorProperty, value);
    }

    public Color LinkColor
    {
        get => (Color)GetValue(LinkColorProperty);
        set => SetValue(LinkColorProperty, value);
    }

    /// <summary>A ticked item: the whole line crossed out and dimmed, whatever its runs say.</summary>
    public bool IsStruck
    {
        get => (bool)GetValue(IsStruckProperty);
        set => SetValue(IsStruckProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public Color PlaceholderColor
    {
        get => (Color)GetValue(PlaceholderColorProperty);
        set => SetValue(PlaceholderColorProperty, value);
    }

    public View? InputAccessory
    {
        get => (View?)GetValue(InputAccessoryProperty);
        set => SetValue(InputAccessoryProperty, value);
    }

    /// <summary>The colour the text is actually drawn in.</summary>
    public Color EffectiveTextColor => IsStruck ? DimColor : TextColor;

    private static void OnIsFocusRequestedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if ((bool)newValue) ((RichTextBlock)bindable).ApplyFocusRequest();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // A block that was created already asking for focus — the line that has just been split off
        // by Enter — gets its handler only now. The request waited; honour it.
        if (Handler != null && IsFocusRequested) ApplyFocusRequest();
    }

    private void ApplyFocusRequest()
    {
        if (Handler is null) return;

        var caret = Caret;
        Dispatcher.Dispatch(() =>
        {
            if (Handler is null || !IsFocusRequested) return;

            Handler.Invoke(nameof(FocusAt), caret);
            IsFocusRequested = false;
        });
    }

    /// <summary>The command name the handler answers with the keyboard and the caret at the given index.</summary>
    public const string FocusAt = "FocusAt";
}
