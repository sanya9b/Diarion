#if IOS || MACCATALYST
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CoreGraphics;
using Diarion.Models.Markdown;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Diarion.Controls.Handlers;

/// <summary>
/// <see cref="RichTextBlock"/> on iOS and Mac Catalyst: a <see cref="UITextView"/> holding an attributed
/// string, with the run's <see cref="InlineStyle"/> carried alongside the font as a private attribute.
/// </summary>
/// <remarks>
/// <para>
/// The private attribute is the whole trick. Reading bold back out of a font descriptor would work
/// until the first heading, where every run is bold already; reading it out of a number the handler
/// put there is exact. UIKit copies every attribute of the character before the caret into what is
/// typed next — that is what <c>typingAttributes</c> is — so the number travels with the text for
/// free, and setting <c>typingAttributes</c> is how "press bold, then type" is done.
/// </para>
/// <para>
/// The formatting bar reaches the text view as its <c>inputAccessoryView</c>: the system attaches it
/// to the top of the keyboard and moves the two together, whatever height the keyboard has today,
/// which is the one arrangement on iOS that never needs to measure the keyboard.
/// </para>
/// </remarks>
public partial class RichTextBlockHandler : ViewHandler<RichTextBlock, RichTextView>
{
    private static readonly NSString StyleKey = new("DiarionInlineStyle");

    protected override RichTextView CreatePlatformView() => new();

    protected override void ConnectHandler(RichTextView platformView)
    {
        base.ConnectHandler(platformView);

        platformView.Changed += OnChanged;
        platformView.SelectionChanged += OnSelectionChanged;
        platformView.Started += OnStarted;
        platformView.Ended += OnEnded;
    }

    protected override void DisconnectHandler(RichTextView platformView)
    {
        platformView.Changed -= OnChanged;
        platformView.SelectionChanged -= OnSelectionChanged;
        platformView.Started -= OnStarted;
        platformView.Ended -= OnEnded;

        base.DisconnectHandler(platformView);
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var width = double.IsInfinity(widthConstraint) ? nfloat.MaxValue : (nfloat)widthConstraint;
        var height = double.IsInfinity(heightConstraint) ? nfloat.MaxValue : (nfloat)heightConstraint;
        var fit = PlatformView.SizeThatFits(new CGSize(width, height));

        return new Size(double.IsInfinity(widthConstraint) ? fit.Width : widthConstraint, fit.Height);
    }

    // --- platform → view model ---

    private void OnChanged(object? sender, EventArgs e)
    {
        var view = VirtualView;
        var platform = PlatformView;

        platform.UpdatePlaceholder();
        PushSelection();
        view.Body = Extract(platform.AttributedText);

        // With nothing left in it the text view forgets its typing attributes and falls back to its
        // plain font; give it ours back, in the style the caret was carrying.
        if (string.IsNullOrEmpty(platform.Text)) platform.TypingAttributes2 = Attributes(view.TypingStyle);

        PushTypingStyle();
        view.InvalidateMeasure();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        PushSelection();
        PushTypingStyle();
    }

    private void OnStarted(object? sender, EventArgs e) => ((IView)VirtualView).IsFocused = true;

    private void OnEnded(object? sender, EventArgs e) => ((IView)VirtualView).IsFocused = false;

    private void PushSelection()
    {
        var range = PlatformView.SelectedRange;
        VirtualView.SelectionStart = (int)range.Location;
        VirtualView.SelectionLength = (int)range.Length;
    }

    // UIKit has already worked out what the caret inherited: it is in the typing attributes.
    private void PushTypingStyle()
    {
        var attributes = PlatformView.TypingAttributes2;
        var style = attributes != null && attributes.TryGetValue(StyleKey, out var value) && value is NSNumber number
            ? (InlineStyle)number.Int32Value
            : InlineStyle.None;

        VirtualView.TypingStyle = style;
    }

    private static StyledText Extract(NSAttributedString? text)
    {
        if (text == null || text.Length == 0) return StyledText.Empty;

        var runs = new List<InlineSpan>();
        text.EnumerateAttribute(StyleKey, new NSRange(0, text.Length), NSAttributedStringEnumeration.None,
            (NSObject value, NSRange range, ref bool stop) =>
            {
                var style = value is NSNumber number ? (InlineStyle)number.Int32Value : InlineStyle.None;
                runs.Add(new InlineSpan(text.Substring(range.Location, range.Length).Value ?? string.Empty, style));
            });

        return StyledText.FromRuns(runs);
    }

    // --- view model → platform ---

    private static void MapBody(RichTextBlockHandler handler, RichTextBlock view)
    {
        // The body usually arrives back as the very text the view just reported. Redrawing then would
        // cost the selection and the composition the keyboard is in the middle of.
        if (Extract(handler.PlatformView.AttributedText).Equals(view.Body)) return;

        handler.Render();
    }

    private static void MapTypingStyle(RichTextBlockHandler handler, RichTextBlock view)
        => handler.PlatformView.TypingAttributes2 = handler.Attributes(view.TypingStyle);

    private static void MapLook(RichTextBlockHandler handler, RichTextBlock view)
    {
        var platform = handler.PlatformView;
        platform.Font = handler.Font(InlineStyle.None);
        platform.TextColor = view.EffectiveTextColor.ToPlatform();
        handler.Render();
    }

    private static void MapPlaceholder(RichTextBlockHandler handler, RichTextBlock view)
    {
        var platform = handler.PlatformView;
        platform.Placeholder.Text = view.Placeholder;
        platform.Placeholder.TextColor = view.PlaceholderColor.ToPlatform();
        platform.Placeholder.Font = handler.Font(InlineStyle.None);
        platform.UpdatePlaceholder();
    }

    private static void MapInputAccessory(RichTextBlockHandler handler, RichTextBlock view)
    {
        var platform = handler.PlatformView;
        var accessory = view.InputAccessory;

        platform.InputAccessoryView = accessory == null || handler.MauiContext == null
            ? null
            : KeyboardAccessoryHost.For(accessory, handler.MauiContext);

        if (platform.IsFirstResponder) platform.ReloadInputViews();
    }

    private static void MapFocusAt(RichTextBlockHandler handler, RichTextBlock view, object? args)
    {
        var platform = handler.PlatformView;
        var caret = Math.Clamp(args is int at ? at : 0, 0, (int)(platform.AttributedText?.Length ?? 0));

        platform.BecomeFirstResponder();
        platform.SelectedRange = new NSRange(caret, 0);
    }

    private void Render()
    {
        var platform = PlatformView;
        var view = VirtualView;

        var selected = platform.SelectedRange;
        var text = new NSMutableAttributedString();
        foreach (var run in view.Body.Runs)
        {
            text.Append(new NSAttributedString(run.Text, Attributes(run.Style)));
        }

        platform.AttributedText = text;

        var location = Math.Min(selected.Location, text.Length);
        platform.SelectedRange = new NSRange(location, Math.Min(selected.Length, text.Length - location));
        platform.UpdatePlaceholder();
        view.InvalidateMeasure();
    }

    private NSDictionary Attributes(InlineStyle style)
    {
        var view = VirtualView;
        var struck = view.IsStruck;

        var colour = view.EffectiveTextColor;
        if (!struck && style.HasFlag(InlineStyle.Code)) colour = view.CodeColor;
        if (!struck && style.HasFlag(InlineStyle.Link)) colour = view.LinkColor;

        var attributes = new NSMutableDictionary
        {
            [UIStringAttributeKey.Font] = Font(style),
            [UIStringAttributeKey.ForegroundColor] = colour.ToPlatform(),
            [StyleKey] = NSNumber.FromInt32((int)style)
        };

        if (struck || style.HasFlag(InlineStyle.Strikethrough))
        {
            attributes[UIStringAttributeKey.StrikethroughStyle] = NSNumber.FromInt32((int)NSUnderlineStyle.Single);
        }

        return attributes;
    }

    private UIFont Font(InlineStyle style)
    {
        var view = VirtualView;
        var size = (nfloat)view.FontSize;
        var bold = style.HasFlag(InlineStyle.Bold) || view.FontAttributes.HasFlag(FontAttributes.Bold);
        var italic = style.HasFlag(InlineStyle.Italic) || view.FontAttributes.HasFlag(FontAttributes.Italic);
        var weight = bold ? UIFontWeight.Bold : UIFontWeight.Regular;

        var font = (style.HasFlag(InlineStyle.Code)
                       ? UIFont.GetMonospacedSystemFont(size, weight)
                       : UIFont.SystemFontOfSize(size, weight))
                   ?? UIFont.SystemFontOfSize(size)!;
        if (!italic) return font;

        var traits = font.FontDescriptor.SymbolicTraits | UIFontDescriptorSymbolicTraits.Italic;
        var descriptor = font.FontDescriptor.CreateWithTraits(traits);
        return descriptor == null ? font : UIFont.FromDescriptor(descriptor, size) ?? font;
    }
}

/// <summary>The text view itself, plus the placeholder UIKit never gave it.</summary>
public sealed class RichTextView : UITextView
{
    public RichTextView()
    {
        ScrollEnabled = false;
        BackgroundColor = UIColor.Clear;
        Placeholder = new UILabel { BackgroundColor = UIColor.Clear, Lines = 0 };
        AddSubview(Placeholder);
    }

    public UILabel Placeholder { get; }

    public void UpdatePlaceholder() => Placeholder.Hidden = !string.IsNullOrEmpty(Text);

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        var inset = TextContainerInset;
        var x = inset.Left + TextContainer.LineFragmentPadding;
        var width = Math.Max(0, Bounds.Width - x - inset.Right - TextContainer.LineFragmentPadding);
        var height = Placeholder.SizeThatFits(new CGSize(width, nfloat.MaxValue)).Height;
        Placeholder.Frame = new CGRect(x, inset.Top, width, height);
    }
}

/// <summary>
/// A MAUI view wrapped so the keyboard can carry it. One host per view, however many text fields
/// point at it: a note is many lines, and the bar must not flicker when the caret moves between them.
/// </summary>
/// <remarks>
/// UIKit sizes an accessory view from its frame, so the height is measured once from the MAUI side and
/// written into the frame; the width follows the keyboard through autoresizing. The MAUI platform
/// view lays its own children out from whatever frame it is given.
/// </remarks>
public sealed class KeyboardAccessoryHost : UIView
{
    private static readonly ConditionalWeakTable<View, KeyboardAccessoryHost> Hosts = new();

    private readonly UIView _content;

    private KeyboardAccessoryHost(View view, IMauiContext context)
    {
        _content = view.ToPlatform(context);
        BackgroundColor = UIColor.Clear;
        AutoresizingMask = UIViewAutoresizing.FlexibleWidth;

        var width = (double)UIScreen.MainScreen.Bounds.Width;
        var size = view.Measure(width, double.PositiveInfinity);

        Frame = new CGRect(0, 0, width, Math.Ceiling(size.Height));
        AddSubview(_content);
    }

    public static KeyboardAccessoryHost For(View view, IMauiContext context)
        => Hosts.GetValue(view, v => new KeyboardAccessoryHost(v, context));

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        _content.Frame = Bounds;
    }
}
#endif
