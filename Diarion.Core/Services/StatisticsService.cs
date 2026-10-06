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

    public async Task<SleepStatistics> GetSleepStatisticsAsync(StatsRange range)
    {
        range = range.Normalized();
        var entries = await _diaryService.GetDiaryEntriesForStatsAsync(range.Start, range.End);
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
        for (var d = range.Start; d <= range.End; d = d.AddDays(1))
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

    public async Task<MoodStatistics> GetMoodStatisticsAsync(StatsRange range)
    {
        range = range.Normalized();
        var entries = await _diaryService.GetDiaryEntriesForStatsAsync(range.Start, range.End);
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

        // Daily mood series (gap-filled so it spans exactly the window): average valence for the trend line
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
        for (var d = range.Start; d <= range.End; d = d.AddDays(1))
        {
            dailyTrend.Add(moodByDate.TryGetValue(d, out var info)
                ? new MoodTrendPoint { Date = d, Valence = info.Valence, HasData = true, DominantEmotion = info.Dominant }
                : new MoodTrendPoint { Date = d, Valence = 0, HasData = false });
        }

        // Hour-of-day profile. Weighted per OBSERVATION, unlike the donut above: the question here is
        // "how do I usually feel at 14:00", which is about hours, not days. Scalar-only days carry no
        // hour and so contribute nothing.
        // Days are tracked as a set of dates, not a counter: several DTO rows can share one calendar day,
        // and the whole point of the figure is how many different days back an hour up.
        var byHour = new Dictionary<int, (int Count, double Sum, HashSet<DateTime> Days)>();
        foreach (var entry in entriesList)
        {
            foreach (var hourMood in MoodAggregate.HourlyObservations(entry.HourlyMood))
            {
                if (!byHour.TryGetValue(hourMood.Hour, out var acc))
                {
                    acc = (0, 0d, new HashSet<DateTime>());
                }

                acc.Days.Add(entry.Date.Date);
                byHour[hourMood.Hour] = (acc.Count + 1, acc.Sum + hourMood.Mood.ToValence(), acc.Days);
            }
        }

        var hourlyProfile = new List<MoodHourPoint>();
        for (int hour = MoodAggregate.FirstHour; hour <= MoodAggregate.LastHour; hour++)
        {
            hourlyProfile.Add(byHour.TryGetValue(hour, out var slot)
                ? new MoodHourPoint { Hour = hour, Valence = slot.Sum / slot.Count, Count = slot.Count, DayCount = slot.Days.Count, HasData = true }
                : new MoodHourPoint { Hour = hour, Valence = 0, Count = 0, DayCount = 0, HasData = false });
        }

        return new MoodStatistics
        {
            EmotionCounts = counts,
            TopEmotion = topEmotion,
            DailyTrend = dailyTrend,
            HourlyProfile = hourlyProfile
        };
    }

    public async Task<TodoStatistics> GetTodoStatisticsAsync(StatsRange range)
    {
        range = range.Normalized();
        // Using the optimized summary method that counts directly in DB
        return await _todoService.GetTodoStatsSummaryAsync(range.Start, range.End);
    }

    public async Task<FinanceStatistics> GetFinanceStatisticsAsync(StatsRange range, Guid? accountId = null)
    {
        range = range.Normalized();
        var startDate = range.Start;
        var end = range.End;

        // Twice the selected window: the comparison card needs the preceding period as a baseline, and
        // fetching it here keeps everything on this screen derived from one read of one set of rows.
        var (fetchStart, _) = ReportPeriod.PreviousWindow(startDate, end);
        var fetched = await _financeService.GetFinanceTransactionsForStatsAsync(fetchStart, end);

        // Scoped in memory rather than in the query on purpose. AccountId is a nullable Guid and LiteDB's
        // LINQ translation is broken for those (see FinanceService.DeleteAccountAsync) — and it fails by
        // returning no rows, which this screen would render as a perfectly plausible empty state. It also
        // makes flipping the account chip free, since the rows are already in hand.
        var scoped = accountId == null
            ? fetched
            : fetched.Where(t => t.AccountId == accountId).ToList();

        var transactions = scoped.Where(t => t.Date >= startDate).ToList();

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

        stats.Trend = FinanceReportCalculator.ComputeTrend(transactions, startDate, end);
        stats.Comparison = FinanceReportCalculator.ComputeComparison(scoped, startDate, end);

        // One account is not a breakdown — the card hides itself rather than showing a single bar.
        if (accountId == null)
        {
            var accounts = await _financeService.GetAccountsAsync(includeArchived: true);
            var transfers = await _financeService.GetTransfersAsync();
            stats.AccountBreakdown = FinanceReportCalculator.ComputeAccountBreakdown(
                accounts, transactions, transfers, startDate, end);
        }

        return stats;
    }
}