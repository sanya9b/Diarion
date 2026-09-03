#if WINDOWS
using System;
using System.Collections.Generic;
using Diarion.Models.Markdown;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Thickness = Microsoft.UI.Xaml.Thickness;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Diarion.Controls.Handlers;

/// <summary>
/// <see cref="RichTextBlock"/> on Windows: a <see cref="RichEditBox"/>, which already knows bold from
/// italic and carries the insertion point's format into what is typed next. The handler's job is
/// mostly translation — its character format into <see cref="InlineStyle"/> and back — plus keeping the
/// control looking like a line of a note rather than a Word document.
/// </summary>
/// <remarks>
/// Bold and italic that belong to the whole line — a heading, a quote — are set as the default format
/// and left out when reading back, so a heading does not report every word as emphasised. The price is
/// that bold inside a heading cannot be told from the heading; it could not be seen either.
/// </remarks>
public partial class RichTextBlockHandler : ViewHandler<RichTextBlock, RichEditBox>
{
    private const string CodeFont = "Consolas";

    private string _baseFont = "Segoe UI";
    private bool _rendering;

    protected override RichEditBox CreatePlatformView()
    {
        var box = new RichEditBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 6, 0, 6),
            MinHeight = 0,
            IsSpellCheckEnabled = true,
            ClipboardCopyFormat = RichEditClipboardFormat.PlainText,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };

        // The stock template paints a light box behind the text, lighter still when hovered or focused.
        // The line has to sit on the page like the label it replaces. RichEditBox shares TextBox's
        // brushes, TextControl*, which is why those are the keys and not RichEditBox*.
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBackgroundDisabled", "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled"
                 })
        {
            box.Resources[key] = clear;
        }
        box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);

        _baseFont = box.Document.GetDefaultCharacterFormat().Name;
        return box;
    }

    protected override void ConnectHandler(RichEditBox platformView)
    {
        base.ConnectHandler(platformView);

        platformView.TextChanged += OnTextChanged;
        platformView.SelectionChanged += OnSelectionChanged;
        platformView.GotFocus += OnGotFocus;
        platformView.LostFocus += OnLostFocus;
        platformView.Paste += OnPaste;
    }

    protected override void DisconnectHandler(RichEditBox platformView)
    {
        platformView.TextChanged -= OnTextChanged;
        platformView.SelectionChanged -= OnSelectionChanged;
        platformView.GotFocus -= OnGotFocus;
        platformView.LostFocus -= OnLostFocus;
        platformView.Paste -= OnPaste;

        base.DisconnectHandler(platformView);
    }

    // --- platform → view model ---

    private void OnTextChanged(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;

        PushSelection();
        VirtualView.Body = Extract();
        PushTypingStyle();
        VirtualView.InvalidateMeasure();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_rendering) return;

        PushSelection();
        PushTypingStyle();
    }

    private void OnGotFocus(object sender, RoutedEventArgs e) => ((IView)VirtualView).IsFocused = true;

    private void OnLostFocus(object sender, RoutedEventArgs e) => ((IView)VirtualView).IsFocused = false;

    // Rich text from elsewhere would arrive with its own fonts and colours, none of which mean anything
    // here. What is pasted is the words, in whatever look the caret was carrying.
    private async void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;

        var content = WinClipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text)) return;

        var text = await content.GetTextAsync();
        PlatformView.Document.Selection.TypeText(text.Replace("\r\n", "\r").Replace('\n', '\r'));
    }

    private void PushSelection()
    {
        var length = TextLength();
        var selection = PlatformView.Document.Selection;
        var start = Math.Clamp(Math.Min(selection.StartPosition, selection.EndPosition), 0, length);
        var end = Math.Clamp(Math.Max(selection.StartPosition, selection.EndPosition), 0, length);

        VirtualView.SelectionStart = start;
        VirtualView.SelectionLength = end - start;
    }

    private void PushTypingStyle()
        => VirtualView.TypingStyle = Style(PlatformView.Document.Selection.CharacterFormat);

    // TOM keeps a final paragraph mark that is not part of the text.
    private int TextLength()
    {
        PlatformView.Document.GetText(TextGetOptions.None, out var text);
        return text.EndsWith('\r') ? text.Length - 1 : text.Length;
    }

    private StyledText Extract()
    {
        var document = PlatformView.Document;
        document.GetText(TextGetOptions.None, out var text);
        var length = text.EndsWith('\r') ? text.Length - 1 : text.Length;
        if (length <= 0) return StyledText.Empty;

        var runs = new List<InlineSpan>(length);
        for (var i = 0; i < length; i++)
        {
            var style = Style(document.GetRange(i, i + 1).CharacterFormat);
            runs.Add(new InlineSpan(text[i] == '\r' ? "\n" : text[i].ToString(), style));
        }

        return StyledText.FromRuns(runs);
    }

    private InlineStyle Style(ITextCharacterFormat format)
    {
        var view = VirtualView;
        var style = InlineStyle.None;

        if (format.Bold == FormatEffect.On && !view.FontAttributes.HasFlag(Microsoft.Maui.Controls.FontAttributes.Bold)) style |= InlineStyle.Bold;
        if (format.Italic == FormatEffect.On && !view.FontAttributes.HasFlag(Microsoft.Maui.Controls.FontAttributes.Italic)) style |= InlineStyle.Italic;
        if (format.Strikethrough == FormatEffect.On && !view.IsStruck) style |= InlineStyle.Strikethrough;
        if (string.Equals(format.Name, CodeFont, StringComparison.OrdinalIgnoreCase)) style |= InlineStyle.Code;
        else if (format.ForegroundColor == view.LinkColor.ToWindowsColor()) style |= InlineStyle.Link;

        return style;
    }

    // --- view model → platform ---

    private static void MapBody(RichTextBlockHandler handler, RichTextBlock view)
    {
        if (handler.Extract().Equals(view.Body)) return;

        handler.Render();
    }

    private static void MapTypingStyle(RichTextBlockHandler handler, RichTextBlock view)
    {
        // With text selected the view model rewrites the body instead; the insertion point's format
        // is only meaningful when there is an insertion point.
        var selection = handler.PlatformView.Document.Selection;
        if (selection.StartPosition != selection.EndPosition) return;

        handler.Apply(selection.CharacterFormat, view.TypingStyle);
    }

    private static void MapLook(RichTextBlockHandler handler, RichTextBlock view)
    {
        var document = handler.PlatformView.Document;
        var format = document.GetDefaultCharacterFormat();
        handler.Apply(format, InlineStyle.None);
        document.SetDefaultCharacterFormat(format);

        handler.PlatformView.Foreground = new SolidColorBrush(view.EffectiveTextColor.ToWindowsColor());
        handler.Render();
    }

    private static void MapPlaceholder(RichTextBlockHandler handler, RichTextBlock view)
    {
        handler.PlatformView.PlaceholderText = view.Placeholder;
        handler.PlatformView.Resources["TextControlPlaceholderForeground"] =
            new SolidColorBrush(view.PlaceholderColor.ToWindowsColor());
        handler.PlatformView.Resources["TextControlPlaceholderForegroundFocused"] =
            new SolidColorBrush(view.PlaceholderColor.ToWindowsColor());
        handler.PlatformView.Resources["TextControlPlaceholderForegroundPointerOver"] =
            new SolidColorBrush(view.PlaceholderColor.ToWindowsColor());
    }

    // No soft keyboard to ride on: the bar stays in the page.
    private static void MapInputAccessory(RichTextBlockHandler handler, RichTextBlock view)
    {
    }

    private static void MapFocusAt(RichTextBlockHandler handler, RichTextBlock view, object? args)
    {
        var platform = handler.PlatformView;
        var caret = Math.Clamp(args is int at ? at : 0, 0, handler.TextLength());

        platform.Focus(FocusState.Programmatic);
        platform.Document.Selection.SetRange(caret, caret);
    }

    private void Render()
    {
        var platform = PlatformView;
        var view = VirtualView;
        var document = platform.Document;

        var selection = document.Selection;
        var start = selection.StartPosition;
        var end = selection.EndPosition;

        _rendering = true;
        try
        {
            document.SetText(TextSetOptions.None, view.Body.Text.Replace('\n', '\r'));

            var length = view.Body.Length;
            Apply(document.GetRange(0, length + 1).CharacterFormat, InlineStyle.None);

            var at = 0;
            foreach (var run in view.Body.Runs)
            {
                if (run.Style != InlineStyle.None)
                {
                    Apply(document.GetRange(at, at + run.Text.Length).CharacterFormat, run.Style);
                }

                at += run.Text.Length;
            }

            selection.SetRange(Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
        }
        finally
        {
            _rendering = false;
        }

        PushSelection();
        view.InvalidateMeasure();
    }

    // Writes a run's look into a format: the line's own weight and slant first, the run's on top.
    private void Apply(ITextCharacterFormat format, InlineStyle style)
    {
        var view = VirtualView;
        var bold = style.HasFlag(InlineStyle.Bold) || view.FontAttributes.HasFlag(Microsoft.Maui.Controls.FontAttributes.Bold);
        var italic = style.HasFlag(InlineStyle.Italic) || view.FontAttributes.HasFlag(Microsoft.Maui.Controls.FontAttributes.Italic);
        var struck = style.HasFlag(InlineStyle.Strikethrough) || view.IsStruck;

        var colour = view.EffectiveTextColor;
        if (!view.IsStruck && style.HasFlag(InlineStyle.Code)) colour = view.CodeColor;
        if (!view.IsStruck && style.HasFlag(InlineStyle.Link)) colour = view.LinkColor;

        format.Size = (float)view.FontSize;
        format.Bold = bold ? FormatEffect.On : FormatEffect.Off;
        format.Italic = italic ? FormatEffect.On : FormatEffect.Off;
        format.Strikethrough = struck ? FormatEffect.On : FormatEffect.Off;
        format.Name = style.HasFlag(InlineStyle.Code) ? CodeFont : _baseFont;
        format.ForegroundColor = colour.ToWindowsColor();
    }
}
#endif
