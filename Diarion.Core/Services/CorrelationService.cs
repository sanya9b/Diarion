using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Computes Pearson correlations between daily mood valence and daily factors, fully on-device.
/// Only associations backed by at least <see cref="MinSampleSize"/> paired days are reported, each
/// with a confidence derived from a Fisher z-test, so weak/noisy links are not presented as facts.
/// The factor set is intentionally small (sleep) for now; the design accepts additional factor
/// series (habits, finance, cycle) without changing the algorithm.
/// </summary>
public class CorrelationService : ICorrelationService
{
    public const int MinSampleSize = 14;

    private readonly IDiaryService _diaryService;

    public CorrelationService(IDiaryService diaryService)
    {
        _diaryService = diaryService;
    }

    public async Task<IReadOnlyList<MoodCorrelation>> GetMoodCorrelationsAsync(int days, int lagDays = 0)
    {
        days = Math.Max(1, days);
        lagDays = Math.Max(0, lagDays);

        // Fetch enough history to pair each factor day with a mood day `lagDays` later.
        var start = DateTime.Today.AddDays(-(days - 1) - lagDays);
        var entries = (await _diaryService.GetDiaryEntriesForStatsAsync(start, DateTime.Today)).ToList();

        var moodByDate = new Dictionary<DateTime, double>();
        var sleepDurationByDate = new Dictionary<DateTime, double>();
        var sleepQualityByDate = new Dictionary<DateTime, double>();

        foreach (var e in entries)
        {
            var d = e.Date.Date;
            // Day-keyed, so this does not add samples — it makes each day's value the mean of the
            // hours actually logged instead of a single snapshot.
            if (MoodAggregate.HasAny(e.Emotion, e.HourlyMood))
            {
                moodByDate[d] = MoodAggregate.Valence(e.Emotion, e.HourlyMood);
            }
            if (e.HasSleepStart && e.HasSleepEnd)
            {
                sleepDurationByDate[d] = SleepHours(e.SleepStart!.Value, e.SleepEnd!.Value);
            }
            if (e.SleepQuality > 0)
            {
                sleepQualityByDate[d] = e.SleepQuality;
            }
        }

        var factors = new (string Key, Dictionary<DateTime, double> Values)[]
        {
            ("SleepDuration", sleepDurationByDate),
            ("SleepQuality", sleepQualityByDate),
        };

        var results = new List<MoodCorrelation>();
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

            var r = Pearson(xs, ys);
            results.Add(new MoodCorrelation
            {
                FactorKey = key,
                Coefficient = r,
                SampleSize = xs.Count,
                LagDays = lagDays,
                Strength = ToStrength(r),
                Confidence = ToConfidence(r, xs.Count)
            });
        }

        return results.OrderByDescending(c => Math.Abs(c.Coefficient)).ToList();
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

    private static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        int n = x.Count;
        double meanX = x.Average();
        double meanY = y.Average();
        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            cov += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }
        if (varX <= 0 || varY <= 0)
        {
            return 0; // no variance in one series -> correlation undefined; treat as none
        }
        return cov / Math.Sqrt(varX * varY);
    }

    private static CorrelationStrength ToStrength(double r)
    {
        var a = Math.Abs(r);
        if (a < 0.1) return CorrelationStrength.Negligible;
        if (a < 0.3) return CorrelationStrength.Weak;
        if (a < 0.5) return CorrelationStrength.Moderate;
        return CorrelationStrength.Strong;
    }

    // Significance via Fisher z-transform, mapped to 1..5 confidence "dots".
    private static int ToConfidence(double r, int n)
    {
        if (n < 4)
        {
            return 1;
        }
        double z = Math.Atanh(Math.Clamp(r, -0.999999, 0.999999));
        double stat = Math.Abs(z) * Math.Sqrt(n - 3);
        if (stat > 3.29) return 5; // p < 0.001
        if (stat > 2.58) return 4; // p < 0.01
        if (stat > 1.96) return 3; // p < 0.05
        if (stat > 1.64) return 2; // p < 0.10
        return 1;
    }
}
