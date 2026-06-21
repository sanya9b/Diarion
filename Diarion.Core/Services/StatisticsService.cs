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

    public StatisticsService(IDiaryService diaryService, ITodoService todoService)
    {
        _diaryService = diaryService;
        _todoService = todoService;
    }

    public async Task<SleepStatistics> GetSleepStatisticsAsync(int days)
    {
        var startDate = DateTime.Today.AddDays(-days);
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

        // Fill gaps with 0 duration for chart
        var fullDataPoints = new List<SleepDataPoint>();
        for (var d = startDate.Date; d <= DateTime.Today; d = d.AddDays(1))
        {
            var pt = dataPoints.FirstOrDefault(p => p.Date.Date == d);
            if (pt != null)
            {
                fullDataPoints.Add(pt);
            }
            else
            {
                fullDataPoints.Add(new SleepDataPoint { Date = d, Duration = TimeSpan.Zero, Quality = 0 });
            }
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
        var startDate = DateTime.Today.AddDays(-days);
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

        foreach (var entry in entriesList)
        {
            if (entry.Emotion != Emotion.None)
            {
                if (counts.ContainsKey(entry.Emotion))
                {
                    counts[entry.Emotion]++;
                }
                else
                {
                    counts[entry.Emotion] = 1;
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

        return new MoodStatistics
        {
            EmotionCounts = counts,
            TopEmotion = topEmotion
        };
    }

    public async Task<TodoStatistics> GetTodoStatisticsAsync(int days)
    {
        var startDate = DateTime.Today.AddDays(-days);
        // Using the optimized summary method that counts directly in DB
        return await _todoService.GetTodoStatsSummaryAsync(startDate, DateTime.Today);
    }
}