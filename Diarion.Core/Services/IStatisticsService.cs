using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IStatisticsService
{
    Task<SleepStatistics> GetSleepStatisticsAsync(StatsRange range);
    Task<MoodStatistics> GetMoodStatisticsAsync(StatsRange range);
    Task<TodoStatistics> GetTodoStatisticsAsync(StatsRange range);
    /// <summary>
    /// Finance figures for <paramref name="range"/>. <paramref name="accountId"/> null means every
    /// account; a value scopes the KPIs, the category donut and the trend to that one account and
    /// leaves the per-account breakdown empty.
    /// </summary>
    Task<FinanceStatistics> GetFinanceStatisticsAsync(StatsRange range, Guid? accountId = null);
}
