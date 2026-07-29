using System;

namespace Diarion.Models;

/// <summary>
/// One guided reflection question. Both the built-in library and anything the user writes live here as
/// rows carrying literal text in each supported language.
///
/// The built-ins are seeded from the resource files once and then stand on their own, which means their
/// wording is frozen per database: correcting a typo or adding a third language reaches existing users
/// only through a migration. That was chosen deliberately in exchange for one uniform list the user can
/// reorder, edit and delete without built-ins being a special case.
/// </summary>
public class GuidedPrompt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TextUk { get; set; } = string.Empty;
    public string TextEn { get; set; } = string.Empty;

    public PromptCategory Category { get; set; }

    /// <summary>
    /// The resource key a seeded row came from, empty for user-written prompts. This is identity, not
    /// text: diary entries written before prompts moved into the database store the resource key in
    /// <see cref="DiaryEntry.PromptResourceKey"/>, and it is how those entries still find their question.
    /// Editing a built-in must therefore keep the key, unlike the habit editor which clears it.
    /// </summary>
    public string ResourceKey { get; set; } = string.Empty;

    /// <summary>Position in the library screen only. The daily rotation deliberately ignores it, so
    /// dragging a row cannot change which question today asks.</summary>
    public int Order { get; set; } = int.MaxValue;

    /// <summary>Seeded rows use <see cref="DateTime.MinValue"/> so they are candidates for every past day.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Today;

    /// <summary>Soft delete: the row stays so entries that already answered it can still show the question.</summary>
    public DateTime? DeletedAt { get; set; }
}
