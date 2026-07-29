using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    /// <summary>Removes the episode containing <paramref name="anyDayOfIt"/>, including days either side of it.</summary>
    Task RemoveEpisodeAsync(DateTime anyDayOfIt);
}
