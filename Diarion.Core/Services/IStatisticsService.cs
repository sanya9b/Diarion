using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public interface IStatisticsService
{
    Task<SleepStatistics> GetSleepStatisticsAsync(int days);
    Task<MoodStatistics> GetMoodStatisticsAsync(int days);
    Task<TodoStatistics> GetTodoStatisticsAsync(int days);
    Task<FinanceStatistics> GetFinanceStatisticsAsync(int days);
}