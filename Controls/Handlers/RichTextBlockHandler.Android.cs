#if ANDROID
using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Util;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.Widget;
using Diarion.Models.Markdown;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Color = Microsoft.Maui.Graphics.Color;

namespace Diarion.Controls.Handlers;

/// <summary>
/// <see cref="RichTextBlock"/> on Android: an <see cref="EditText"/> whose runs are spans of one private
/// class carrying the <see cref="InlineStyle"/> they draw.
/// </summary>
/// <remarks>
/// <para>
/// Android has no typing attributes. A span grows when text is inserted strictly inside it and not
/// when text is typed at its edge — which is where most typing happens — so the handler keeps the
/// typing style itself: it is read off the character before the caret whenever the caret moves, flipped
/// by the formatting bar, and stamped onto exactly the characters each edit inserted.
/// </para>
/// <para>
/// "Exactly the characters inserted" is worked out by diffing what the edit replaced against what it
/// put there. The keyboard's autocorrect replaces a whole word to add one letter, and styling the
/// whole word would be wrong the moment the typing style differed from the word's.
/// </para>
/// </remarks>
public partial class RichTextBlockHandler : ViewHandler<RichTextBlock, RichEditText>
{
    private const InlineStyle AllStyles =
        InlineStyle.Bold | InlineStyle.Italic | InlineStyle.Strikethrough | InlineStyle.Code | InlineStyle.Link;

    private Watcher? _watcher;
    private InlineStyle _typingStyle;
    private bool _rendering;
    private bool _changing;

    protected override RichEditText CreatePlatformView() => new(Context);

    protected override void ConnectHandler(RichEditText platformView)
    {
        base.ConnectHandler(platformView);

        _watcher = new Watcher(this);
        platformView.AddTextChangedListener(_watcher);
        platformView.SelectionMoved += OnSelectionMoved;
        platformView.FocusChange += OnFocusChange;
    }

    protected override void DisconnectHandler(RichEditText platformView)
    {
        if (_watcher != null) platformView.RemoveTextChangedListener(_watcher);
        _watcher = null;
        platformView.SelectionMoved -= OnSelectionMoved;
        platformView.FocusChange -= OnFocusChange;

        base.DisconnectHandler(platformView);
    }

    // --- platform → view model ---

    private void OnTextEdited(IEditable editable, int insertedAt, int insertedCount)
    {
        var view = VirtualView;
        var body = Extract(editable);

        if (insertedCount > 0)
        {
            var stamped = body
                .WithStyle(insertedAt, insertedCount, AllStyles, on: false)
                .WithStyle(insertedAt, insertedCount, _typingStyle, on: true);

            if (!stamped.Equals(body))
            {
                body = stamped;
                ApplySpans(editable, body);
            }
        }

        PushSelection();
        view.Body = body;
        view.TypingStyle = _typingStyle;
        view.InvalidateMeasure();
    }

    private void OnSelectionMoved(object? sender, EventArgs e)
    {
        if (_rendering || _changing) return;

        PushSelection();

        // The caret moved on its own: what is typed next inherits the character before it.
        var editable = PlatformView.EditableText;
        var caret = PlatformView.SelectionStart;
        _typingStyle = editable == null ? InlineStyle.None : StyleBefore(editable, caret);
        VirtualView.TypingStyle = _typingStyle;
    }

    private void OnFocusChange(object? sender, Android.Views.View.FocusChangeEventArgs e)
        => ((IView)VirtualView).IsFocused = e.HasFocus;

    private void PushSelection()
    {
        var start = Math.Min(PlatformView.SelectionStart, PlatformView.SelectionEnd);
        var end = Math.Max(PlatformView.SelectionStart, PlatformView.SelectionEnd);
        VirtualView.SelectionStart = start;
        VirtualView.SelectionLength = end - start;
    }

    private static InlineStyle StyleBefore(ISpanned text, int caret)
    {
        if (caret <= 0) return InlineStyle.None;

        var style = InlineStyle.None;
        foreach (var span in text.GetSpans(caret - 1, caret, Java.Lang.Class.FromType(typeof(InlineMarkSpan))) ?? [])
        {
            if (span is InlineMarkSpan mark && text.GetSpanStart(mark) <= caret - 1 && text.GetSpanEnd(mark) >= caret)
            {
                style |= mark.Style;
            }
        }

        return style;
    }

    private static StyledText Extract(ISpanned? text)
    {
        if (text == null || text.Length() == 0) return StyledText.Empty;

        var plain = text.ToString() ?? string.Empty;
        var marks = Java.Lang.Class.FromType(typeof(InlineMarkSpan));
        var runs = new List<InlineSpan>();

        var at = 0;
        while (at < plain.Length)
        {
            var next = text.NextSpanTransition(at, plain.Length, marks);
            var style = InlineStyle.None;
            foreach (var span in text.GetSpans(at, next, marks) ?? [])
            {
                if (span is InlineMarkSpan mark && text.GetSpanStart(mark) <= at && text.GetSpanEnd(mark) >= next)
                {
                    style |= mark.Style;
                }
            }

            runs.Add(new InlineSpan(plain.Substring(at, next - at), style));
            at = next;
        }

        return StyledText.FromRuns(runs);
    }

    // --- view model → platform ---

    private static void MapBody(RichTextBlockHandler handler, RichTextBlock view)
    {
        if (Extract(handler.PlatformView.EditableText).Equals(view.Body)) return;

        handler.Render();
    }

    private static void MapTypingStyle(RichTextBlockHandler handler, RichTextBlock view)
        => handler._typingStyle = view.TypingStyle;

    private static void MapLook(RichTextBlockHandler handler, RichTextBlock view)
    {
        var platform = handler.PlatformView;
        var fonts = handler.GetRequiredService<IFontManager>();

        var weight = view.FontAttributes.HasFlag(FontAttributes.Bold) ? FontWeight.Bold : FontWeight.Regular;
        var slant = view.FontAttributes.HasFlag(FontAttributes.Italic) ? FontSlant.Italic : FontSlant.Default;
        var font = Microsoft.Maui.Font.OfSize(null, view.FontSize, weight, slant);

        platform.Typeface = fonts.GetTypeface(font);
        var size = fonts.GetFontSize(font);
        platform.SetTextSize(size.Unit, size.Value);
        platform.SetTextColor(view.EffectiveTextColor.ToPlatform());
        platform.PaintFlags = view.IsStruck
            ? platform.PaintFlags | PaintFlags.StrikeThruText
            : platform.PaintFlags & ~PaintFlags.StrikeThruText;

        handler.Render();
    }

    private static void MapPlaceholder(RichTextBlockHandler handler, RichTextBlock view)
    {
        handler.PlatformView.Hint = view.Placeholder;
        handler.PlatformView.SetHintTextColor(view.PlaceholderColor.ToPlatform());
    }

    // The keyboard's accessory view is an iOS arrangement. Android moves the page up instead, and the
    // bar laid out under the note is already above the keyboard.
    private static void MapInputAccessory(RichTextBlockHandler handler, RichTextBlock view)
    {
    }

    private static void MapFocusAt(RichTextBlockHandler handler, RichTextBlock view, object? args)
    {
        var platform = handler.PlatformView;
        var length = platform.EditableText?.Length() ?? 0;
        var caret = Math.Clamp(args is int at ? at : 0, 0, length);

        platform.RequestFocus();
        platform.SetSelection(caret);
        platform.Post(() =>
        {
            var manager = platform.Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;
            manager?.ShowSoftInput(platform, ShowFlags.Implicit);
        });
    }

    private void Render()
    {
        var platform = PlatformView;
        var view = VirtualView;

        var start = platform.SelectionStart;
        var end = platform.SelectionEnd;

        _rendering = true;
        try
        {
            var text = new SpannableStringBuilder(view.Body.Text);
            ApplySpans(text, view.Body);
            platform.SetText(text, TextView.BufferType.Editable);

            var length = view.Body.Length;
            platform.SetSelection(Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
        }
        finally
        {
            _rendering = false;
        }

        PushSelection();
        view.InvalidateMeasure();
    }

    // Restyles without touching the characters, so the keyboard's composing state and the caret both
    // survive: span changes are not text changes and wake no watcher.
    private void ApplySpans(ISpannable text, StyledText body)
    {
        foreach (var old in text.GetSpans(0, text.Length(), Java.Lang.Class.FromType(typeof(InlineMarkSpan))) ?? [])
        {
            text.RemoveSpan(old);
        }

        var view = VirtualView;
        var baseTypeface = PlatformView.Typeface;
        var at = 0;
        foreach (var run in body.Runs)
        {
            if (run.Style != InlineStyle.None)
            {
                Color? colour = null;
                if (!view.IsStruck && run.Has(InlineStyle.Code)) colour = view.CodeColor;
                if (!view.IsStruck && run.Has(InlineStyle.Link)) colour = view.LinkColor;

                text.SetSpan(new InlineMarkSpan(run.Style, baseTypeface, colour), at, at + run.Text.Length, SpanTypes.ExclusiveExclusive);
            }

            at += run.Text.Length;
        }
    }

    /// <summary>Watches edits and works out which characters each one actually added.</summary>
    private sealed class Watcher(RichTextBlockHandler handler) : Java.Lang.Object, ITextWatcher
    {
        private string _replaced = string.Empty;
        private int _insertedAt;
        private int _insertedCount;

        public void BeforeTextChanged(Java.Lang.ICharSequence? s, int start, int count, int after)
        {
            handler._changing = true;
            _replaced = s?.ToString()?.Substring(start, count) ?? string.Empty;
        }

        public void OnTextChanged(Java.Lang.ICharSequence? s, int start, int before, int count)
        {
            var inserted = s?.ToString()?.Substring(start, count) ?? string.Empty;

            // Autocorrect swaps a whole word for the same word plus a letter. The letter is what was
            // typed; the rest keeps whatever look it already had.
            var prefix = 0;
            while (prefix < _replaced.Length && prefix < inserted.Length && _replaced[prefix] == inserted[prefix]) prefix++;

            var suffix = 0;
            while (suffix < _replaced.Length - prefix && suffix < inserted.Length - prefix
                   && _replaced[_replaced.Length - 1 - suffix] == inserted[inserted.Length - 1 - suffix]) suffix++;

            _insertedAt = start + prefix;
            _insertedCount = inserted.Length - prefix - suffix;
        }

        public void AfterTextChanged(IEditable? s)
        {
            handler._changing = false;
            if (s == null || handler._rendering) return;

            handler.OnTextEdited(s, _insertedAt, _insertedCount);
        }
    }
}

/// <summary>The field, with the one thing <see cref="EditText"/> keeps to itself made public: where the caret went.</summary>
public sealed class RichEditText : AppCompatEditText
{
    public RichEditText(Context context) : base(context)
    {
        Background = null;
        SetPadding(0, 0, 0, 0);
        Gravity = GravityFlags.Top | GravityFlags.Start;
        InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine
                    | InputTypes.TextFlagCapSentences | InputTypes.TextFlagAutoCorrect;
        SetHorizontallyScrolling(false);
        ImeOptions = (ImeAction)ImeFlags.NoExtractUi;
    }

    public event EventHandler? SelectionMoved;

    protected override void OnSelectionChanged(int selStart, int selEnd)
    {
        base.OnSelectionChanged(selStart, selEnd);
        SelectionMoved?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>One run's look, and the <see cref="InlineStyle"/> it stands for so it can be read back exactly.</summary>
public sealed class InlineMarkSpan : MetricAffectingSpan
{
    private readonly Typeface? _typeface;
    private readonly Android.Graphics.Color? _colour;
    private readonly bool _struck;

    public InlineMarkSpan(InlineStyle style, Typeface? baseTypeface, Color? colour)
    {
        Style = style;
        _colour = colour?.ToPlatform();
        _struck = style.HasFlag(InlineStyle.Strikethrough);

        var bold = style.HasFlag(InlineStyle.Bold);
        var italic = style.HasFlag(InlineStyle.Italic);
        var shape = bold && italic ? TypefaceStyle.BoldItalic
            : bold ? TypefaceStyle.Bold
            : italic ? TypefaceStyle.Italic
            : TypefaceStyle.Normal;

        var family = style.HasFlag(InlineStyle.Code) ? Typeface.Monospace : baseTypeface;
        _typeface = family == null && shape == TypefaceStyle.Normal ? null : Typeface.Create(family, shape);
    }

    public InlineStyle Style { get; }

    public override void UpdateMeasureState(TextPaint paint) => ApplyTypeface(paint);

    public override void UpdateDrawState(TextPaint? paint)
    {
        if (paint == null) return;

        ApplyTypeface(paint);
        if (_struck) paint.StrikeThruText = true;
        if (_colour.HasValue) paint.Color = _colour.Value;
    }

    private void ApplyTypeface(TextPaint paint)
    {
        if (_typeface == null) return;

        // Keep whatever weight the line already had — a heading is bold before any run says so.
        var current = paint.Typeface?.Style ?? TypefaceStyle.Normal;
        var merged = Typeface.Create(_typeface, current | _typeface.Style);
        paint.SetTypeface(merged);
    }
}
#endif
