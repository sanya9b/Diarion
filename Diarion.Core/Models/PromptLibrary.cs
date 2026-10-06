using System;
using System.Collections.Generic;
using System.Linq;

namespace Diarion.Models;

/// <summary>
/// An immutable snapshot of the prompt collection, read once and handed to whoever needs to resolve or
/// pick a prompt. Keeping the whole library in memory is what lets <see cref="Services.PromptSelector"/>
/// stay a pure function instead of reaching into the database.
/// </summary>
public sealed class PromptLibrary
{
    public static readonly PromptLibrary Empty = new(Array.Empty<GuidedPrompt>());

    private readonly IReadOnlyList<GuidedPrompt> _all;

    public PromptLibrary(IEnumerable<GuidedPrompt> prompts)
    {
        _all = prompts?.ToList() ?? new List<GuidedPrompt>();
    }

    /// <summary>Every row, deleted ones included — an entry that answered a since-deleted prompt must
    /// still be able to show the question it answered.</summary>
    public IReadOnlyList<GuidedPrompt> All => _all;

    /// <summary>Rows the library screen shows, in the user's chosen order.</summary>
    public IReadOnlyList<GuidedPrompt> Ordered =>
        _all.Where(p => p.DeletedAt == null).OrderBy(p => p.Order).ThenBy(p => p.Id).ToList();

    /// <summary>
    /// What the rotation may offer for a given day. Ordered canonically rather than by
    /// <see cref="GuidedPrompt.Order"/> so that reordering the library screen cannot silently change
    /// which question a day asks.
    /// </summary>
    public IReadOnlyList<GuidedPrompt> Candidates(PromptCategory category, DateTime date)
    {
        var day = date.Date;
        return _all
            .Where(p => p.Category == category && p.DeletedAt == null && p.CreatedAt.Date <= day)
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToList();
    }

    /// <summary>
    /// Resolves the reference stored on a diary entry. New entries store the row id; entries written
    /// before prompts moved into the database store a resource key, and the seeded rows still carry it.
    /// </summary>
    public GuidedPrompt? Find(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;

        if (Guid.TryParse(reference, out var id))
            return _all.FirstOrDefault(p => p.Id == id);

        return _all.FirstOrDefault(p => string.Equals(p.ResourceKey, reference, StringComparison.Ordinal));
    }

    /// <summary>The reference to persist on a diary entry for the given prompt.</summary>
    public static string ReferenceFor(GuidedPrompt? prompt) => prompt?.Id.ToString() ?? string.Empty;
}
