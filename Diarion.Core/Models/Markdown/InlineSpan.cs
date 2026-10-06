using System;

namespace Diarion.Models.Markdown;

/// <summary>What a stretch of text inside one line is marked up as.</summary>
[Flags]
public enum InlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strikethrough = 4,
    /// <summary>Between backticks.</summary>
    Code = 8,
    /// <summary>The title inside <c>[[…]]</c>; the brackets are not part of the text.</summary>
    Link = 16
}

/// <summary>
/// A run of characters within a line that share one look. A line with no markup is a single span with
/// <see cref="InlineStyle.None"/>, which is the common case and costs nothing to draw.
/// </summary>
public class InlineSpan
{
    public InlineSpan()
    {
    }

    public InlineSpan(string text, InlineStyle style = InlineStyle.None)
    {
        Text = text;
        Style = style;
    }

    /// <summary>The visible text — the <c>**</c>, the backticks and the brackets are already off.</summary>
    public string Text { get; set; } = string.Empty;

    public InlineStyle Style { get; set; }

    public bool Has(InlineStyle style) => (Style & style) == style;
}
