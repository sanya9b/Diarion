using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Computes Pearson correlations between daily mood valence and daily factors, fully on-device.
/// <para>
/// This is the one place where keeping eight domains in a single app pays for itself: sleep, cycle,
/// habits, meals, money and tasks are all measured against the same mood series, which no
/// single-purpose competitor can do because it only holds one of them.
/// </para>
/// <para>
/// Breadth is also the danger. Testing many factors at p &lt; 0.05 manufactures a false positive on
/// most days, so every pass is corrected with Benjamini-Hochberg and the dots the user sees come from
/// the adjusted value. Only associations backed by at least <see cref="MinSampleSize"/> paired days
/// are reported at all.
/// </para>
/// </summary>
public class CorrelationService : ICorrelationService
{
    public const int MinSampleSize = 14;

    /// <summary>Factor keys, so the service and its consumers cannot disagree about spelling.</summary>
    public static class Factors
    {
        public const string SleepDuration = "SleepDuration";
        public const string SleepQuality = "SleepQuality";
        public const string CyclePeriodDay = "CyclePeriodDay";
        public const string CycleSymptomLoad = "CycleSymptomLoad";
        public const string HabitCompletion = "HabitCompletion";
        public const string MealsLogged = "MealsLogged";
        public const string TaskCompletion = "TaskCompletion";
        public const string DailySpend = "DailySpend";
    }

    private readonly IDiaryService _diaryService;
    private readonly ICycleLogService _cycleLogService;
    private readonly IProfileService _profileService;
    private readonly ITodoService _todoService;
    private readonly IFinanceService _financeService;

    public CorrelationService(
        IDiaryService diaryService,
        ICycleLogService cycleLogService,
        IProfileService profileService,
        ITodoService todoService,
        IFinanceService financeService)
    {
        _diaryService = diaryService;
        _cycleLogService = cycleLogService;
        _profileService = profileService;
        _todoService = todoService;
        _financeService = financeService;
    }

    /// <summary>
    /// Loads the mood series and every factor series for the window. Shared by the correlation pass
    /// and the readiness check so the two can never disagree about how much data there is.
    /// </summary>
    private async Task<(Dictionary<DateTime, double> Mood, List<(string Key, Dictionary<DateTime, double> Values)> Factors)>
        BuildSeriesAsync(int days, int lagDays)
    {
        // Fetch enough history to pair each factor day with a mood day `lagDays` later.
        var start = DateTime.Today.AddDays(-(days - 1) - lagDays);
        var entries = (await _diaryService.GetDiaryEntriesForStatsAsync(start, DateTime.Today)).ToList();

        var moodByDate = new Dictionary<DateTime, double>();
        foreach (var e in entries)
        {
            // Day-keyed, so this does not add samples — it makes each day's value the mean of the
            // hours actually logged instead of a single snapshot.
            if (MoodAggregate.HasAny(e.Emotion, e.HourlyMood))
            {
                moodByDate[e.Date.Date] = MoodAggregate.Valence(e.Emotion, e.HourlyMood);
            }
        }

        var factors = BuildDiaryFactors(entries);
        factors.AddRange(await BuildCycleFactorsAsync(start));
        factors.AddRange(await BuildTaskFactorAsync(start));
        factors.AddRange(await BuildSpendFactorAsync(start));

        return (moodByDate, factors);
    }

    /// <summary>Days where this factor and a mood were both recorded, after applying the lag.</summary>
    private static int PairedCount(Dictionary<DateTime, double> values, Dictionary<DateTime, double> mood, int lagDays)
        => values.Count(kv => mood.ContainsKey(kv.Key.AddDays(lagDays)));

    public async Task<CorrelationReadiness> GetReadinessAsync(int days, int lagDays = 0)
    {
        days = Math.Max(1, days);
        lagDays = Math.Max(0, lagDays);

        var (moodByDate, factors) = await BuildSeriesAsync(days, lagDays);

        // The best any single factor manages, because one factor clearing the bar is enough to show
        // a first insight.
        var best = factors.Count == 0
            ? 0
            : factors.Max(f => PairedCount(f.Values, moodByDate, lagDays));

        return new CorrelationReadiness(best, MinSampleSize);
    }

    public async Task<IReadOnlyList<MoodCorrelation>> GetMoodCorrelationsAsync(int days, int lagDays = 0)
    {
        days = Math.Max(1, days);
        lagDays = Math.Max(0, lagDays);

        var (moodByDate, factors) = await BuildSeriesAsync(days, lagDays);

        var candidates = new List<MoodCorrelation>();
        foreach (var (key, values) in factors)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var kv in values)
            {
                if (moodByDate.TryGetValue(kv.Key.AddDays(lagDays), out var mood))
                {
                    xs.Add(kv.Value);
                    ys.Add(mood);
                }
            }

            if (xs.Count < MinSampleSize)
            {
                continue;
            }

            var r = CorrelationStatistics.Pearson(xs, ys);
            candidates.Add(new MoodCorrelation
            {
                FactorKey = key,
                Coefficient = r,
                SampleSize = xs.Count,
                LagDays = lagDays,
                Strength = ToStrength(r),
                PValue = CorrelationStatistics.PValue(r, xs.Count)
            });
        }

        // Correction spans exactly the factors that had enough data to be tested. Including the ones
        // that were skipped would penalise findings for tests that never ran.
        var adjusted = CorrelationStatistics.BenjaminiHochberg(candidates.Select(c => c.PValue).ToList());
        for (var i = 0; i < candidates.Count; i++)
        {
            candidates[i].AdjustedPValue = adjusted[i];
            candidates[i].Confidence = CorrelationStatistics.ConfidenceDots(adjusted[i]);
        }

        return candidates.OrderByDescending(c => Math.Abs(c.Coefficient)).ToList();
    }

    /// <summary>
    /// Factors that come straight off the diary entries already loaded. Each series only carries the
    /// days that actually recorded the thing — a day with no habits configured is not a day with zero
    /// habits done, and treating it as one would drag every coefficient toward nothing.
    /// </summary>
    private static List<(string Key, Dictionary<DateTime, double> Values)> BuildDiaryFactors(
        IReadOnlyList<DiaryEntryStatsDto> entries)
    {
        var sleepDuration = new Dictionary<DateTime, double>();
        var sleepQuality = new Dictionary<DateTime, double>();
        var habitCompletion = new Dictionary<DateTime, double>();
        var mealsLogged = new Dictionary<DateTime, double>();

        foreach (var e in entries)
        {
            var d = e.Date.Date;

            if (e.HasSleepStart && e.HasSleepEnd)
            {
                sleepDuration[d] = SleepHours(e.SleepStart!.Value, e.SleepEnd!.Value);
            }

            if (e.SleepQuality > 0)
            {
                sleepQuality[d] = e.SleepQuality;
            }

            if (e.HabitCompletion is { } completion)
            {
                habitCompletion[d] = completion;
            }

            if (e.MealsLogged > 0)
            {
                // Zero is ambiguous — it means both "ate nothing" and "did not fill this in" — so days
                // without a single meal ticked stay out rather than being counted as fasting.
                mealsLogged[d] = e.MealsLogged;
            }
        }

        return new List<(string, Dictionary<DateTime, double>)>
        {
            (Factors.SleepDuration, sleepDuration),
            (Factors.SleepQuality, sleepQuality),
            (Factors.HabitCompletion, habitCompletion),
            (Factors.MealsLogged, mealsLogged),
        };
    }

    /// <summary>
    /// Share of the day's planned tasks that got done. Only days that had at least one task count —
    /// a day with nothing planned has no completion rate, and scoring it zero would say the user
    /// failed at something they never set out to do.
    /// </summary>
    private async Task<List<(string Key, Dictionary<DateTime, double> Values)>> BuildTaskFactorAsync(DateTime start)
    {
        var todos = (await _todoService.GetTodosForStatsAsync(start, DateTime.Today)).ToList();
        if (todos.Count == 0)
        {
            return new List<(string, Dictionary<DateTime, double>)>();
        }

        var byDate = todos
            .GroupBy(t => t.TargetDate.Date)
            .ToDictionary(g => g.Key, g => (double)g.Count(t => t.IsCompleted) / g.Count());

        return new List<(string, Dictionary<DateTime, double>)>
        {
            (Factors.TaskCompletion, byDate),
        };
    }

    /// <summary>
    /// Money spent per day. Unlike the other factors a day with no rows is a real zero, so the series
    /// is filled in — but only from the first transaction the user ever recorded. Padding zeros back
    /// before that would invent frugal days out of days when the feature was simply unused.
    /// </summary>
    private async Task<List<(string Key, Dictionary<DateTime, double> Values)>> BuildSpendFactorAsync(DateTime start)
    {
        var transactions = (await _financeService.GetFinanceTransactionsForStatsAsync(start, DateTime.Today))
            .Where(t => t.Type == TransactionType.Expense)
            .ToList();

        if (transactions.Count == 0)
        {
            return new List<(string, Dictionary<DateTime, double>)>();
        }

        var spentByDate = transactions
            .GroupBy(t => t.Date.Date)
            .ToDictionary(g => g.Key, g => (double)g.Sum(t => t.Amount));

        var from = transactions.Min(t => t.Date.Date);
        if (from < start.Date)
        {
            from = start.Date;
        }

        var series = new Dictionary<DateTime, double>();
        for (var d = from; d <= DateTime.Today; d = d.AddDays(1))
        {
            series[d] = spentByDate.TryGetValue(d, out var amount) ? amount : 0;
        }

        return new List<(string, Dictionary<DateTime, double>)>
        {
            (Factors.DailySpend, series),
        };
    }

    /// <summary>
    /// Cycle factors, or nothing at all when the feature is off — a correlation against a period the
    /// user is not tracking would be built from an all-zero series anyway.
    ///
    /// Both series are keyed on every day in the window, not only on the days with a log row: a period
    /// day is only meaningful against the days that were not one, and a dictionary of nothing but ones
    /// has no variance for Pearson to work with.
    /// </summary>
    private async Task<List<(string Key, Dictionary<DateTime, double> Values)>> BuildCycleFactorsAsync(DateTime start)
    {
        var empty = new List<(string, Dictionary<DateTime, double>)>();

        var profile = await _profileService.GetUserProfileAsync();
        if (profile?.IsCycleTrackingActive != true)
        {
            return empty;
        }

        var logs = await _cycleLogService.GetLogsAsync();
        if (logs is not { Count: > 0 })
        {
            return empty;
        }

        var byDate = logs.GroupBy(l => l.Date.Date).ToDictionary(g => g.Key, g => g.First());

        // Only span days the user has actually been logging; padding zeros back to the start of the
        // window before they ever opened the feature would invent "not a period day" observations.
        var firstLogged = logs.Min(l => l.Date.Date);
        var from = firstLogged > start.Date ? firstLogged : start.Date;

        var periodDay = new Dictionary<DateTime, double>();
        var symptomLoad = new Dictionary<DateTime, double>();

        for (var d = from; d <= DateTime.Today; d = d.AddDays(1))
        {
            byDate.TryGetValue(d, out var log);
            periodDay[d] = log is { IsSymptomOnly: false } ? 1 : 0;
            symptomLoad[d] = log?.Symptoms?.Count ?? 0;
        }

        return new List<(string, Dictionary<DateTime, double>)>
        {
            (Factors.CyclePeriodDay, periodDay),
            (Factors.CycleSymptomLoad, symptomLoad),
        };
    }

    private static double SleepHours(TimeSpan start, TimeSpan end)
    {
        var duration = end - start;
        if (duration.TotalHours < 0)
        {
            duration = duration.Add(TimeSpan.FromHours(24));
        }
        return duration.TotalHours;
    }

    private static CorrelationStrength ToStrength(double r)
    {
        var a = Math.Abs(r);
        if (a < 0.1) return CorrelationStrength.Negligible;
        if (a < 0.3) return CorrelationStrength.Weak;
        if (a < 0.5) return CorrelationStrength.Moderate;
        return CorrelationStrength.Strong;
    }
}
