using System.Collections.Generic;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface ICorrelationService
{
    /// <summary>
    /// Computes on-device correlations between daily factors and mood over the last
    /// <paramref name="days"/> days. <paramref name="lagDays"/> shifts the factor earlier than the
    /// mood (e.g. 1 = yesterday's factor vs today's mood). Only factors with enough paired days are
    /// returned, ranked by strength. These are associations, not proven causes.
    /// </summary>
    Task<IReadOnlyList<MoodCorrelation>> GetMoodCorrelationsAsync(int days, int lagDays = 0);
}
