using System;
using System.Threading.Tasks;

namespace Diarion.Core.Services;

public interface IHealthDataService
{
    Task<bool> IsSupportedAsync();
    Task<bool> RequestPermissionsAsync();
    Task<(TimeSpan? SleepStart, TimeSpan? SleepEnd)> GetSleepDataAsync(DateTime targetDate);
}
