using System.ComponentModel;
using Diarion.Models.Markdown;
using Diarion.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Diarion.Controls;

/// <summary>
/// One line of a note: a label showing what the markup means, and the field it is typed in, swapped
/// on focus. Only the look is set from outside — everything the line says comes from the block it is
/// bound to.
/// </summary>
public partial class NoteBlockBody : ContentView
{
    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(NoteBlockBody), 18d, propertyChanged: OnLookChanged);

    public static readonly BindableProperty FontAttributesProperty =
        BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(NoteBlockBody), Microsoft.Maui.Controls.FontAttributes.None, propertyChanged: OnLookChanged);

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(NoteBlockBody), null, propertyChanged: OnLookChanged);

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(NoteBlockBody), string.Empty);

    private NoteBlockViewModel? _block;

    public NoteBlockBody()
    {
        InitializeComponent();
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }

    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    /// <summary>Shown when the line is empty — and only on the empty line of an empty note.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>The colour the field actually draws with: a ticked item is quiet as well as crossed out.</summary>
    public Color EffectiveTextColor => _block?.IsStruck == true ? Dim : Ink;

    private Color Ink => TextColor ?? Theme("Theme_Midnight_Snow", Colors.White);
    private Color Dim => Theme("Theme_Ocean_Dust", Colors.Gray);

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_block != null) _block.PropertyChanged -= OnBlockChanged;
        _block = BindingContext as NoteBlockViewModel;
        if (_block != null) _block.PropertyChanged += OnBlockChanged;

        Redraw();
    }

    private void OnBlockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoteBlockViewModel.Spans) or nameof(NoteBlockViewModel.IsStruck))
        {
            Redraw();
        }
    }

    private static void OnLookChanged(BindableObject bindable, object oldValue, object newValue)
        => ((NoteBlockBody)bindable).Redraw();

    private void Redraw()
    {
        OnPropertyChanged(nameof(EffectiveTextColor));

        var formatted = new FormattedString();
        var struck = _block?.IsStruck == true;
        var ink = struck ? Dim : Ink;

        foreach (var span in _block?.Spans ?? [])
        {
            var attributes = FontAttributes;
            if (span.Has(InlineStyle.Bold)) attributes |= Microsoft.Maui.Controls.FontAttributes.Bold;
            if (span.Has(InlineStyle.Italic)) attributes |= Microsoft.Maui.Controls.FontAttributes.Italic;

            var decorations = TextDecorations.None;
            if (struck || span.Has(InlineStyle.Strikethrough)) decorations |= TextDecorations.Strikethrough;

            // A ticked item is one grey crossed-out line: colouring its links and code inside that
            // would say the line still wants reading.
            var colour = ink;
            if (!struck && span.Has(InlineStyle.Code)) colour = Theme("Theme_Amber", ink);
            if (!struck && span.Has(InlineStyle.Link)) colour = Theme("Theme_Coral", ink);

            formatted.Spans.Add(new Span
            {
                Text = span.Text,
                FontSize = FontSize,
                FontAttributes = attributes,
                TextDecorations = decorations,
                TextColor = colour
            });
        }

        FormattedLabel.FormattedText = formatted;
    }

    private static Color Theme(string key, Color fallback)
        => Application.Current?.Resources?.TryGetValue(key, out var value) == true && value is Color colour
            ? colour
            : fallback;
}
