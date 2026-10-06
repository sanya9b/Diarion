using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface ICycleLogService
{
    /// <summary>Every day marked as a period day, ascending. The whole history — it is a few dozen rows a year.</summary>
    Task<List<DateTime>> GetMarkedDatesAsync();

    /// <summary>
    /// Records a period of <paramref name="length"/> days starting on <paramref name="start"/>, skipping
    /// days already recorded so re-adding an overlapping range cannot duplicate rows. Days in the future
    /// are ignored: a period cannot be reported before it happens.
    /// </summary>
    Task AddEpisodeAsync(DateTime start, int length);

    /// <summary>
    /// Removes the episode containing <paramref name="anyDayOfIt"/>, including days either side of it.
    /// Days that also carry symptoms are kept as symptom-only rows rather than deleted.
    /// </summary>
    Task RemoveEpisodeAsync(DateTime anyDayOfIt);

    /// <summary>Every row, period days and symptom-only days alike, ascending.</summary>
    Task<List<CycleLog>> GetLogsAsync();

    /// <summary>
    /// Replaces the symptoms recorded for <paramref name="date"/>. Creates a symptom-only row when the day
    /// is not already logged, and drops a symptom-only row once its last symptom is cleared.
    /// </summary>
    Task SetSymptomsAsync(DateTime date, IEnumerable<string> symptoms);
}
