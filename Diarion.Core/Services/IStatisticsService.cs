using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IStatisticsService
{
    Task<SleepStatistics> GetSleepStatisticsAsync(int days);
    Task<MoodStatistics> GetMoodStatisticsAsync(int days);
    Task<TodoStatistics> GetTodoStatisticsAsync(int days);
    /// <summary>
    /// Finance figures for the last <paramref name="days"/> days. <paramref name="accountId"/> null means
    /// every account; a value scopes the KPIs, the category donut and the trend to that one account and
    /// leaves the per-account breakdown empty.
    /// </summary>
    Task<FinanceStatistics> GetFinanceStatisticsAsync(int days, Guid? accountId = null);
}