using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Diarion.Services.Ai;

/// <param name="IsRefusal">True when the answer cannot be trusted and must be replaced by one.</param>
/// <param name="Text">The model's answer, or empty on a refusal.</param>
/// <param name="Used">Only the citations the answer actually referred to, in the order shown.</param>
public sealed record ChatAnswer(bool IsRefusal, string Text, IReadOnlyList<ChatCitation> Used);

/// <summary>
/// Checks a generated answer against the passages it was given.
/// </summary>
/// <remarks>
/// This is where "never a guess" is enforced, mechanically. An answer citing nothing is an answer
/// the model produced from its own weights rather than from the diary, and it is discarded — a
/// prompt instruction cannot make that guarantee, a downgrade can.
/// </remarks>
public static partial class CitationParser
{
    [GeneratedRegex(@"\[(\d{1,2})\]")]
    private static partial Regex MarkerPattern { get; }

    public static ChatAnswer Parse(string? answer, IReadOnlyList<ChatCitation> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);

        if (string.IsNullOrWhiteSpace(answer))
        {
            return new ChatAnswer(true, string.Empty, []);
        }

        var byMarker = offered.ToDictionary(c => c.Marker);
        var cited = new List<ChatCitation>();

        foreach (Match match in MarkerPattern.Matches(answer))
        {
            if (!int.TryParse(match.Groups[1].Value, out var marker))
            {
                continue;
            }

            // A marker outside what was offered is invented. Dropping it rather than trusting it is
            // the difference between a citation and a decoration.
            if (byMarker.TryGetValue(marker, out var citation) && !cited.Contains(citation))
            {
                cited.Add(citation);
            }
        }

        return cited.Count == 0
            ? new ChatAnswer(true, string.Empty, [])
            : new ChatAnswer(false, answer.Trim(), cited);
    }
}
