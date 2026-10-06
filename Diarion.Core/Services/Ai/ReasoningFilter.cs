using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Diarion.Services.Ai;

/// <summary>
/// Removes a reasoning model's private monologue from what it produces.
/// </summary>
/// <remarks>
/// Qwen3 is a hybrid reasoning model: unless told otherwise it writes out its deliberation between
/// <c>&lt;think&gt;</c> tags before answering. Two things go wrong when that survives — the user
/// reads a page of English reasoning instead of one Ukrainian sentence, and
/// <see cref="CitationParser"/> harvests every marker the model merely weighed rather than the ones
/// it stood behind. Both were seen in the running app.
/// <para>
/// The prompt carries <c>/no_think</c>, which asks. This is what guarantees.
/// </para>
/// </remarks>
public sealed partial class ReasoningFilter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock { get; }

    /// <summary>Text seen but not yet released: it may be the front of a tag split across pieces.</summary>
    private readonly StringBuilder _buffer = new();

    private bool _inside;

    /// <summary>
    /// Feeds one streamed piece in and returns the part of it that is safe to show. Empty is the
    /// normal answer while the model is still thinking.
    /// </summary>
    public string Push(string? piece)
    {
        if (string.IsNullOrEmpty(piece))
        {
            return string.Empty;
        }

        _buffer.Append(piece);

        var text = _buffer.ToString();
        var safe = new StringBuilder();
        var cursor = 0;

        while (cursor < text.Length)
        {
            if (_inside)
            {
                var end = text.IndexOf(Close, cursor, StringComparison.Ordinal);
                if (end < 0)
                {
                    // The cursor stays put so the tail below can still spot a closing tag that has
                    // only half arrived. Everything before it is reasoning and is dropped either way.
                    break;
                }

                cursor = end + Close.Length;
                _inside = false;
                continue;
            }

            var open = text.IndexOf(Open, cursor, StringComparison.Ordinal);
            var strayClose = text.IndexOf(Close, cursor, StringComparison.Ordinal);

            // A close tag with no open before it means the template pre-filled the open tag and the
            // model began mid-monologue. Only the tag is dropped here; what already streamed cannot
            // be recalled, which is why the answer of record goes through Strip and not through this.
            if (strayClose >= 0 && (open < 0 || strayClose < open))
            {
                safe.Append(text, cursor, strayClose - cursor);
                cursor = strayClose + Close.Length;
                continue;
            }

            if (open < 0)
            {
                break;
            }

            safe.Append(text, cursor, open - cursor);
            cursor = open + Open.Length;
            _inside = true;
        }

        var rest = text[cursor..];
        var held = _inside ? PartialTagAtEnd(rest, Close) : PartialTagAtEnd(rest, Open, Close);

        if (!_inside)
        {
            safe.Append(rest, 0, rest.Length - held);
        }

        _buffer.Clear();
        _buffer.Append(rest, rest.Length - held, held);

        return safe.ToString();
    }

    /// <summary>
    /// Called once the stream ends. Reasoning that was cut off before it closed yields nothing —
    /// a truncated monologue is not half an answer, and the empty result becomes an honest refusal.
    /// </summary>
    public string Flush()
    {
        var rest = _inside ? string.Empty : _buffer.ToString();
        _buffer.Clear();
        _inside = false;
        return rest;
    }

    /// <summary>
    /// Strips reasoning from a whole answer. Used for the text the citations are read from, where
    /// the stream's one-way nature does not apply and every case can be resolved.
    /// </summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var stripped = ThinkBlock.Replace(text, string.Empty);

        // At most one unmatched tag of each kind can survive that, and the two cannot pair up.
        // Anything before a close is what the model was thinking; anything after an unclosed open
        // is thinking that ran out of tokens.
        var close = stripped.LastIndexOf(Close, StringComparison.Ordinal);
        if (close >= 0)
        {
            stripped = stripped[(close + Close.Length)..];
        }

        var open = stripped.IndexOf(Open, StringComparison.Ordinal);
        if (open >= 0)
        {
            stripped = stripped[..open];
        }

        return stripped.Trim();
    }

    /// <summary>
    /// Length of the longest suffix of <paramref name="text"/> that could still grow into one of
    /// <paramref name="tags"/>. Whole tags are already consumed by the caller, so only the front of
    /// one can be here.
    /// </summary>
    private static int PartialTagAtEnd(string text, params string[] tags)
    {
        var longest = 0;
        foreach (var tag in tags)
        {
            longest = Math.Max(longest, tag.Length - 1);
        }

        for (var length = Math.Min(text.Length, longest); length > 0; length--)
        {
            var tail = text.AsSpan(text.Length - length);
            foreach (var tag in tags)
            {
                if (tag.AsSpan().StartsWith(tail, StringComparison.Ordinal))
                {
                    return length;
                }
            }
        }

        return 0;
    }
}
