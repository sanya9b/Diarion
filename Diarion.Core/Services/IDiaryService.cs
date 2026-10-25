using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IDiaryService
{
    Task<List<DiaryEntry>> GetAllEntriesAsync();
    Task<DiaryEntry> GetEntryForDateAsync(DateTime date);
    Task<DiaryEntry> GetEntryByIdAsync(Guid id);
    Task SaveEntryAsync(DiaryEntry entry);
    Task DeleteEntryAsync(Guid id);
    Task<StreakResult> GetCurrentStreakAsync();

    /// <summary>
    /// The days in the range on which something was actually written — the same "has content" rule the
    /// streak counts by, so the statistics' activity card and the streak tile can never disagree.
    /// Dates only, ascending.
    /// </summary>
    Task<IReadOnlyList<DateTime>> GetContentDatesAsync(DateTime startDate, DateTime endDate);

    // Statistics (Optimized DB Queries)
    Task<IEnumerable<DiaryEntryStatsDto>> GetDiaryEntriesForStatsAsync(DateTime startDate, DateTime endDate);

    /// <summary>Every answered guided prompt, newest first. Unanswered days are left out.</summary>
    Task<IReadOnlyList<PromptAnswerDto>> GetPromptAnswersAsync();
}