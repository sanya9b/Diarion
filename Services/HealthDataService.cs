using System;
using System.Threading.Tasks;
using Diarion.Core.Services;

#if IOS
using HealthKit;
using Foundation;
#endif

#if ANDROID
using AndroidX.Health.Connect.Client;
using AndroidX.Health.Connect.Client.Records;
using AndroidX.Health.Connect.Client.Request;
using AndroidX.Health.Connect.Client.Time;
#endif

namespace Diarion.Services;

public class HealthDataService : IHealthDataService
{
    public Task<bool> IsSupportedAsync()
    {
#if IOS
        return Task.FromResult(HKHealthStore.IsHealthDataAvailable);
#else
        // Android reports unsupported on purpose. Reading Health Connect needs Kotlin coroutine interop
        // that is not written yet, and the placeholder that stood here returned invented sleep times
        // which the caller wrote straight into the user's diary entry. An unavailable feature is honest;
        // a feature that fabricates personal history is not. Flip this back on with the real read.
        return Task.FromResult(false);
#endif
    }

    public async Task<bool> RequestPermissionsAsync()
    {
#if IOS
        if (!HKHealthStore.IsHealthDataAvailable) return false;
        var healthStore = new HKHealthStore();
        var sleepType = HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis);
        if (sleepType == null) return false;
        
        var typesToRead = new NSSet(sleepType);
        var tcs = new TaskCompletionSource<bool>();
        healthStore.RequestAuthorizationToShare(new NSSet(), typesToRead, (success, error) =>
        {
            tcs.TrySetResult(success);
        });
        return await tcs.Task;
#else
        // Never claim a grant that was never asked for. Android needs an ActivityResult launcher from
        // MainActivity, which arrives with the real Health Connect read.
        return await Task.FromResult(false);
#endif
    }

    public async Task<(TimeSpan? SleepStart, TimeSpan? SleepEnd)> GetSleepDataAsync(DateTime targetDate)
    {
#if IOS
        if (!HKHealthStore.IsHealthDataAvailable) return (null, null);
        
        var healthStore = new HKHealthStore();
        var sleepType = HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis);
        if (sleepType == null) return (null, null);

        // Usually sleep starts the evening before
        var startDate = targetDate.Date.AddHours(-12);
        var endDate = targetDate.Date.AddHours(12);
        
        var predicate = HKQuery.GetPredicateForSamples((NSDate)startDate, (NSDate)endDate, HKQueryOptions.None);
        var sortDescriptor = new NSSortDescriptor(HKSample.SortIdentifierStartDate, true);
        
        var tcs = new TaskCompletionSource<(TimeSpan?, TimeSpan?)>();
        var query = new HKSampleQuery(sleepType, predicate, 100, new[] { sortDescriptor }, (q, results, err) => 
        {
            if (results != null && results.Length > 0)
            {
                var first = results[0];
                var last = results[results.Length - 1];
                
                var start = ((DateTime)first.StartDate).TimeOfDay;
                var end = ((DateTime)last.EndDate).TimeOfDay;
                tcs.TrySetResult((start, end));
            }
            else
            {
                tcs.TrySetResult((null, null));
            }
        });
        
        healthStore.ExecuteQuery(query);
        return await tcs.Task;
#else
        // Unreachable while IsSupportedAsync says no, and empty rather than invented if it ever is.
        return await Task.FromResult<(TimeSpan?, TimeSpan?)>((null, null));
#endif
    }
}
