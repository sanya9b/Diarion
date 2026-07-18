using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Diarion.Services;

/// <summary>
/// Pure parsing helpers for Obsidian-style note syntax: <c>#tags</c> and <c>[[links]]</c>.
/// Kept UI- and storage-agnostic so it can be unit-tested and reused by the service layer.
/// </summary>
public static class NoteParser
{
    // '#' must sit at the start or after whitespace (so "C#" doesn't tag), and the tag must start with a
    // letter/underscore (so "item #1" doesn't create a numeric junk tag). Trailing digits/_/-// allowed.
    private static readonly Regex TagRegex = new(
        @"(?<![^\s])#([\p{L}_][\p{L}\p{N}_/\-]*)", RegexOptions.Compiled);

    // [[Anything not containing brackets]].
    private static readonly Regex LinkRegex = new(
        @"\[\[([^\[\]]+)\]\]", RegexOptions.Compiled);

    /// <summary>Distinct, lowercased tags (without the leading '#') in first-seen order.</summary>
    public static List<string> ExtractTags(string? content)
    {
        if (string.IsNullOrEmpty(content)) return new List<string>();

        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (Match m in TagRegex.Matches(content))
        {
            var tag = m.Groups[1].Value.ToLowerInvariant();
            if (seen.Add(tag)) result.Add(tag);
        }
        return result;
    }

    /// <summary>Distinct normalized link targets (for <c>[[Title]]</c>) in first-seen order.</summary>
    public static List<string> ExtractLinks(string? content)
    {
        if (string.IsNullOrEmpty(content)) return new List<string>();

        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (Match m in LinkRegex.Matches(content))
        {
            var title = NormalizeTitle(m.Groups[1].Value);
            if (title.Length == 0) continue;
            if (seen.Add(title)) result.Add(title);
        }
        return result;
    }

    /// <summary>
    /// Original-cased, trimmed <c>[[link]]</c> titles for display (distinct by normalized key,
    /// first-seen casing wins). Use <see cref="ExtractLinks"/> for the normalized storage/query form.
    /// </summary>
    public static List<string> ExtractLinkDisplayTitles(string? content)
    {
        if (string.IsNullOrEmpty(content)) return new List<string>();

        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (Match m in LinkRegex.Matches(content))
        {
            var raw = m.Groups[1].Value.Trim();
            if (raw.Length == 0) continue;
            if (seen.Add(NormalizeTitle(raw))) result.Add(raw);
        }
        return result;
    }

    /// <summary>Matching key for a note title: trimmed and lower-invariant.</summary>
    public static string NormalizeTitle(string? title)
        => (title ?? string.Empty).Trim().ToLowerInvariant();
}
