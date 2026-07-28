using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

public class StatisticsService : IStatisticsService
{
    private readonly IDiaryService _diaryService;
    private readonly ITodoService _todoService;
    private readonly IFinanceService _financeService;

    public StatisticsService(IDiaryService diaryService, ITodoService todoService, IFinanceService financeService)
    {
        _diaryService = diaryService;
        _todoService = todoService;
        _financeService = financeService;
    }

    // A "N day" window means exactly N calendar days INCLUDING today: [today-(N-1), today].
    private static DateTime GetStartDate(int days) => DateTime.Today.AddDays(-(Math.Max(1, days) - 1));

    public async Task<SleepStatistics> GetSleepStatisticsAsync(int days)
    {
        var startDate = GetStartDate(days);
        var entries = await _diaryService.GetDiaryEntriesForStatsAsync(startDate, DateTime.Today);
        var entriesList = entries.ToList();

        var validSleepEntries = entriesList.Where(x => x.HasSleepStart && x.HasSleepEnd).ToList();
        var validQualityEntries = entriesList.Where(x => x.SleepQuality > 0).ToList();

        double averageQuality = validQualityEntries.Count > 0
            ? validQualityEntries.Average(x => x.SleepQuality)
            : 0;

        TimeSpan averageDuration = TimeSpan.Zero;
        var dataPoints = new List<SleepDataPoint>();

        if (validSleepEntries.Count > 0)
        {
            double totalHours = 0;
            foreach (var x in validSleepEntries.OrderBy(e => e.Date))
            {
                var duration = x.SleepEnd!.Value - x.SleepStart!.Value;
                if (duration.TotalHours < 0)
                {
                    duration = duration.Add(TimeSpan.FromHours(24));
                }
                totalHours += duration.TotalHours;

                dataPoints.Add(new SleepDataPoint
                {
                    Date = x.Date,
                    Duration = duration,
                    Quality = x.SleepQuality
                });
            }
            averageDuration = TimeSpan.FromHours(totalHours / validSleepEntries.Count);
        }

        // Fill gaps with 0 duration for chart (O(days) lookup via a date-keyed map)
        var byDate = new Dictionary<DateTime, SleepDataPoint>();
        foreach (var p in dataPoints)
        {
            byDate[p.Date.Date] = p;
        }

        var fullDataPoints = new List<SleepDataPoint>();
        for (var d = startDate.Date; d <= DateTime.Today; d = d.AddDays(1))
        {
            fullDataPoints.Add(byDate.TryGetValue(d, out var pt)
                ? pt
                : new SleepDataPoint { Date = d, Duration = TimeSpan.Zero, Quality = 0 });
        }

        return new SleepStatistics
        {
            AverageSleepDuration = averageDuration,
            AverageSleepQuality = averageQuality,
            DailyData = fullDataPoints
        };
    }

    public async Task<MoodStatistics> GetMoodStatisticsAsync(int days)
    {
        var startDate = GetStartDate(days);
        var entries = await _diaryService.GetDiaryEntriesForStatsAsync(startDate, DateTime.Today);
        var entriesList = entries.ToList();

        var counts = new Dictionary<Emotion, int>();
        foreach (var emotion in Enum.GetValues<Emotion>())
        {
            if (emotion != Emotion.None)
            {
                counts[emotion] = 0;
            }
        }

        // One observation per DAY, not per hour: the donut reads as "share of your days", and
        // hour-weighting would silently turn it into "share of your time" and let one heavily-logged
        // day outweigh a month. For days with no hourly data Dominant returns the scalar, so this is
        // identical to the previous behaviour.
        foreach (var entry in entriesList)
        {
            var dominant = MoodAggregate.Dominant(entry.Emotion, entry.HourlyMood);
            if (dominant != Emotion.None)
            {
                if (counts.ContainsKey(dominant))
                {
                    counts[dominant]++;
                }
                else
                {
                    counts[dominant] = 1;
                }
            }
        }

        var topEmotion = Emotion.None;
        int maxCount = 0;
        foreach (var kvp in counts)
        {
            if (kvp.Value > maxCount)
            {
                maxCount = kvp.Value;
                topEmotion = kvp.Key;
            }
        }

        // Daily mood series (gap-filled so it spans exactly N days): average valence for the trend line
        // plus the day's dominant emotion (mode, deterministic tie-break) for the Year-in-Pixels heatmap.
        var moodByDate = entriesList
            .Where(e => MoodAggregate.HasAny(e.Emotion, e.HourlyMood))
            .GroupBy(e => e.Date.Date)
            .ToDictionary(
                g => g.Key,
                g => (
                    // Averaged across every logged hour, so the trend line reflects the whole day
                    // rather than whichever moment happened to be captured.
                    Valence: g.Average(e => MoodAggregate.Valence(e.Emotion, e.HourlyMood)),
                    Dominant: MoodAggregate.Dominant(
                        g.First().Emotion,
                        g.SelectMany(e => e.HourlyMood).ToList())));

        var dailyTrend = new List<MoodTrendPoint>();
        for (var d = startDate.Date; d <= DateTime.Today; d = d.AddDays(1))
        {
            dailyTrend.Add(moodByDate.TryGetValue(d, out var info)
                ? new MoodTrendPoint { Date = d, Valence = info.Valence, HasData = true, DominantEmotion = info.Dominant }
                : new MoodTrendPoint { Date = d, Valence = 0, HasData = false });
        }

        return new MoodStatistics
        {
            EmotionCounts = counts,
            TopEmotion = topEmotion,
            DailyTrend = dailyTrend
        };
    }

    public async Task<TodoStatistics> GetTodoStatisticsAsync(int days)
    {
        var startDate = GetStartDate(days);
        // Using the optimized summary method that counts directly in DB
        return await _todoService.GetTodoStatsSummaryAsync(startDate, DateTime.Today);
    }

    public async Task<FinanceStatistics> GetFinanceStatisticsAsync(int days)
    {
        var startDate = GetStartDate(days);
        var transactions = await _financeService.GetFinanceTransactionsForStatsAsync(startDate, DateTime.Today);
        
        var stats = new FinanceStatistics();
        
        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();
        var incomes = transactions.Where(t => t.Type == TransactionType.Income).ToList();
        
        stats.TotalExpense = expenses.Sum(e => e.Amount);
        stats.TotalIncome = incomes.Sum(i => i.Amount);
        
        var defaultColors = new[] { "#E07A5F", "#3D405B", "#81B29A", "#F2CC8F", "#E9C46A", "#2A9D8F", "#264653" };
        
        if (stats.TotalExpense > 0)
        {
            var grouped = expenses.GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "Other" : e.Category)
                                  .Select(g => new CategoryStatItem
                                  {
                                      Category = g.Key,
                                      Amount = g.Sum(x => x.Amount),
                                      Percentage = (double)(g.Sum(x => x.Amount) / stats.TotalExpense)
                                  })
                                  .OrderByDescending(x => x.Amount)
                                  .ToList();
                                  
            for (int i = 0; i < grouped.Count; i++)
            {
                grouped[i].ColorHex = defaultColors[i % defaultColors.Length];
            }
            stats.ExpenseByCategory = grouped;
        }

        if (stats.TotalIncome > 0)
        {
            var grouped = incomes.GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "Other" : e.Category)
                                 .Select(g => new CategoryStatItem
                                 {
                                     Category = g.Key,
                                     Amount = g.Sum(x => x.Amount),
                                     Percentage = (double)(g.Sum(x => x.Amount) / stats.TotalIncome)
                                 })
                                 .OrderByDescending(x => x.Amount)
                                 .ToList();
                                 
            var incomeColors = new[] { "#81B29A", "#2A9D8F", "#F2CC8F" };
            for (int i = 0; i < grouped.Count; i++)
            {
                grouped[i].ColorHex = incomeColors[i % incomeColors.Length];
            }
            stats.IncomeByCategory = grouped;
        }

        return stats;
    }
}