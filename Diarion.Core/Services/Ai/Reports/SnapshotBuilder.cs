using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Resources.Localization;

namespace Diarion.Services.Ai.Reports;

/// <summary>
/// Assembles the period snapshot out of services that already exist.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here queries the database directly, and that is a rule rather than an accident. The
/// statistics the report talks about must be the same statistics the statistics screen draws — if the
/// snapshot recomputed the average night's sleep its own way, the report would eventually contradict
/// the chart above it, and the user would have no way to tell which one was lying.
/// </para>
/// <para>
/// Everything that leaves here is rounded and sorted. Sorted so that the same period always produces
/// the same bytes, which is what makes the preview screen a promise rather than an illustration;
/// rounded because seventeen significant figures of mood valence are noise the user is paying tokens
/// to transmit.
/// </para>
/// </remarks>
public sealed class SnapshotBuilder : ISnapshotBuilder
{
    /// <summary>
    /// Lags asked of the correlation engine. Same-day answers "what goes with what"; one-day answers
    /// "what does yesterday do to today", which is the only one of the two a person can act on next
    /// week. Both come off the same window, so the second costs a pass over data already in memory.
    /// </summary>
    private static readonly int[] Lags = { 0, 1 };

    private readonly IStatisticsService _statistics;
    private readonly ICorrelationService _correlations;
    private readonly IDiaryService _diary;
    private readonly IHabitService _habits;
    private readonly ICycleLogService _cycle;
    private readonly IProfileService _profile;
    private readonly IGuidedPromptService _prompts;

    public SnapshotBuilder(
        IStatisticsService statistics,
        ICorrelationService correlations,
        IDiaryService diary,
        IHabitService habits,
        ICycleLogService cycle,
        IProfileService profile,
        IGuidedPromptService prompts)
    {
        _statistics = statistics;
        _correlations = correlations;
        _diary = diary;
        _habits = habits;
        _cycle = cycle;
        _profile = profile;
        _prompts = prompts;
    }

    public async Task<PeriodSnapshot> BuildAsync(
        PeriodKind kind,
        StatsRange range,
        SnapshotOptions options,
        CancellationToken cancellationToken = default)
    {
        range = range.Normalized();

        // None of the services below take a token — they predate this one and are used by screens that
        // cannot cancel. So cancellation is checked between them instead: a year's snapshot is a dozen
        // reads and a loop over 365 days, and abandoning it at the next boundary is quick enough.
        cancellationToken.ThrowIfCancellationRequested();
        var profile = await _profile.GetUserProfileAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var sleep = await _statistics.GetSleepStatisticsAsync(range);

        cancellationToken.ThrowIfCancellationRequested();
        var mood = await _statistics.GetMoodStatisticsAsync(range);

        cancellationToken.ThrowIfCancellationRequested();
        var todos = await _statistics.GetTodoStatisticsAsync(range);

        cancellationToken.ThrowIfCancellationRequested();
        var finance = await _statistics.GetFinanceStatisticsAsync(range);

        var correlations = new List<SnapshotCorrelation>();
        foreach (var lag in Lags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = await _correlations.GetMoodCorrelationsAsync(range, lag);
            correlations.AddRange(found.Select(ToSnapshot));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var habits = await _habits.GetHabitCompletionsAsync(range.Start, range.End);

        cancellationToken.ThrowIfCancellationRequested();
        var trackers = await _habits.GetHarmfulHabitTrackersAsync();

        cancellationToken.ThrowIfCancellationRequested();
        var days = await BuildDaysAsync(range, options, cancellationToken);

        List<SnapshotCycleDay>? cycle = null;
        if (options.IncludeCycle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cycle = BuildCycle(await _cycle.GetLogsAsync(), range);
        }

        return new PeriodSnapshot
        {
            PeriodKind = kind.ToString().ToLowerInvariant(),
            Start = Day(range.Start),
            End = Day(range.End),
            DayCount = range.Days,
            Language = CurrentLanguage(),
            Currency = profile.GetEffectiveCurrencyCode(),
            Sleep = BuildSleep(sleep),
            Mood = BuildMood(mood),
            Tasks = new SnapshotTasks { Completed = todos.CompletedCount, Total = todos.TotalCount },
            Finance = BuildFinance(finance),

            // Strongest first regardless of which lag found it, then by name and lag so that two runs
            // over the same week cannot reorder ties.
            Correlations = correlations
                .OrderByDescending(c => Math.Abs(c.Coefficient))
                .ThenBy(c => c.Factor, StringComparer.Ordinal)
                .ThenBy(c => c.LagDays)
                .ToList(),

            Habits = BuildHabits(habits, trackers, range),
            Days = days,
            Cycle = cycle
        };
    }

    private static string Day(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The language the report must come back in, read the same way every other localized string in
    /// Core reads it. Not a profile field: the user's answer to "what language is this app in" is the
    /// app's language, and a second setting could only ever disagree with the first.
    /// </summary>
    private static string CurrentLanguage()
        => (AppResources.Culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;

    private static SnapshotSleep BuildSleep(SleepStatistics stats) => new()
    {
        AverageHours = Math.Round(stats.AverageSleepDuration.TotalHours, 2),
        AverageQuality = Math.Round(stats.AverageSleepQuality, 2),
        Daily = stats.DailyData
            .OrderBy(p => p.Date)
            .Select(p => new SnapshotSleepDay
            {
                Date = Day(p.Date),

                // The statistics layer fills gaps with zero so the chart has a point to skip. Zero is
                // the wrong thing to send: a night nobody logged is not a night without sleep.
                Hours = p.Duration > TimeSpan.Zero ? Math.Round(p.Duration.TotalHours, 2) : null,
                Quality = p.Quality > 0 ? Math.Round(p.Quality, 2) : null
            })
            .ToList()
    };

    private static SnapshotMood BuildMood(MoodStatistics stats) => new()
    {
        Daily = stats.DailyTrend
            .OrderBy(p => p.Date)
            .Select(p => new SnapshotMoodDay
            {
                Date = Day(p.Date),
                Valence = p.HasData ? Math.Round(p.Valence, 2) : null,
                DominantEmotion = p.HasData && p.DominantEmotion != Emotion.None
                    ? p.DominantEmotion.ToString()
                    : null
            })
            .ToList(),

        ByHour = stats.HourlyProfile
            .Where(h => h.HasData)
            .OrderBy(h => h.Hour)
            .Select(h => new SnapshotMoodHour
            {
                Hour = h.Hour,
                Valence = Math.Round(h.Valence, 2),
                Observations = h.Count,
                Days = h.DayCount
            })
            .ToList(),

        // A dictionary's enumeration order is an implementation detail, and this one is keyed by an
        // enum, so the sort is what makes two runs of the same week produce the same bytes.
        Emotions = stats.EmotionCounts
            .Where(kv => kv.Key != Emotion.None && kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
            .Select(kv => new SnapshotEmotionCount { Emotion = kv.Key.ToString(), Count = kv.Value })
            .ToList(),

        TopEmotion = stats.TopEmotion == Emotion.None ? string.Empty : stats.TopEmotion.ToString()
    };

    /// <summary>
    /// Away from zero rather than the framework's default to-even. Half a kopiyka is not a statistical
    /// quantity to be rounded fairly across many samples — it is what the finance screen shows, and a
    /// figure in the report that disagrees with the screen by a kopiyka is a figure the user stops
    /// trusting the rest of.
    /// </summary>
    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static SnapshotFinance BuildFinance(FinanceStatistics stats) => new()
    {
        Income = Money(stats.TotalIncome),
        Expense = Money(stats.TotalExpense),
        ExpenseByCategory = ToAmounts(stats.ExpenseByCategory),
        IncomeByCategory = ToAmounts(stats.IncomeByCategory),

        // No baseline means the window before this one is empty, and every comparison against nothing
        // reads as infinite growth. Sending null lets the model say nothing rather than say that.
        VersusPrevious = stats.Comparison.HasBaseline
            ? new SnapshotComparison
            {
                PreviousIncome = Money(stats.Comparison.Income.Previous),
                PreviousExpense = Money(stats.Comparison.Expense.Previous)
            }
            : null
    };

    private static List<SnapshotAmount> ToAmounts(IEnumerable<CategoryStatItem> items) => items
        .OrderByDescending(i => i.Amount)
        .ThenBy(i => i.Category, StringComparer.Ordinal)
        .Select(i => new SnapshotAmount { Label = i.Category, Amount = Money(i.Amount) })
        .ToList();

    private static SnapshotHabits BuildHabits(
        IEnumerable<HabitCompletionHistory> habits,
        IEnumerable<HarmfulHabitTracker> trackers,
        StatsRange range) => new()
    {
        Good = habits
            .Select(h => new SnapshotHabit
            {
                Name = h.Name,
                CompletedDays = h.CompletedDates.Count(d => InRange(d, range)),
                ScheduledDays = CountScheduled(h, range)
            })
            .OrderBy(h => h.Name, StringComparer.Ordinal)
            .ToList(),

        Quitting = trackers
            .Select(t => new SnapshotQuitTracker
            {
                Name = t.HarmfulHabitName,
                MarkedDays = (t.MarkedDays ?? new List<DateTime>()).Count(d => InRange(d, range)),
                Relapses = (t.Relapses ?? new List<RelapseEvent>()).Count(r => InRange(r.Date, range))
            })
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList()
    };

    private static bool InRange(DateTime date, StatsRange range)
        => date.Date >= range.Start && date.Date <= range.End;

    /// <summary>
    /// Days in the window the habit was actually due. Completed-out-of-seven would be wrong for
    /// anything not tracked daily: three ticks on a Monday-Wednesday-Friday habit is a perfect week,
    /// and reporting it as 3/7 invites the model to call a perfect week a failure.
    /// </summary>
    private static int CountScheduled(HabitCompletionHistory habit, StatsRange range)
    {
        var from = habit.CreatedAt.Date > range.Start ? habit.CreatedAt.Date : range.Start;
        var count = 0;

        for (var day = from; day <= range.End; day = day.AddDays(1))
        {
            if (habit.Schedule?.IsOccurrenceOn(day) != false) count++;
        }

        return count;
    }

    private async Task<List<SnapshotDay>> BuildDaysAsync(
        StatsRange range,
        SnapshotOptions options,
        CancellationToken cancellationToken)
    {
        // One read of the diary rather than one per day. Asking for each date separately is a scan of
        // the whole collection each time, which a week survives and a year does not.
        var entries = await _diary.GetAllEntriesAsync();
        var byDate = new Dictionary<DateTime, DiaryEntry>();
        foreach (var entry in entries)
        {
            byDate.TryAdd(entry.Date.Date, entry);
        }

        var library = await _prompts.GetLibraryAsync();
        var days = new List<SnapshotDay>(range.Days);

        for (var day = range.Start; day <= range.End; day = day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!byDate.TryGetValue(day, out var entry))
            {
                // The day still gets a row. It is a day the person did not write, and that is a fact
                // about the period rather than an absence of one.
                days.Add(new SnapshotDay { Date = Day(day) });
                continue;
            }

            var answer = Trimmed(entry.PromptAnswer);

            days.Add(new SnapshotDay
            {
                Date = Day(day),
                Title = Trimmed(entry.Title),
                Text = Trimmed(entry.Content),
                Gratitude = Trimmed(entry.Gratitude),
                SoulFood = Trimmed(entry.SoulFood),
                Triggers = Trimmed(entry.Triggers),
                SupportForOthers = Trimmed(entry.SupportForOthers),

                // The question only travels when it was answered — an unanswered prompt says nothing
                // about the day beyond which question the rotation happened to offer.
                Prompt = answer is null
                    ? null
                    : Trimmed(PromptLocalization.ResolveText(library.Find(entry.PromptResourceKey))),
                PromptAnswer = answer,

                SleepNotes = Trimmed(entry.SleepNotes),
                IntimateLife = options.IncludeIntimateLife ? Trimmed(entry.IntimateLife) : null
            });
        }

        return days;
    }

    /// <summary>Blank becomes null, so the serializer drops the field instead of sending an empty string.</summary>
    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<SnapshotCycleDay> BuildCycle(IEnumerable<CycleLog> logs, StatsRange range) => logs
        .Where(l => InRange(l.Date, range))
        .OrderBy(l => l.Date)
        .Select(l => new SnapshotCycleDay
        {
            Date = Day(l.Date),
            IsPeriodDay = !l.IsSymptomOnly,
            Symptoms = (l.Symptoms ?? new List<string>()).OrderBy(s => s, StringComparer.Ordinal).ToList()
        })
        .ToList();

    private static SnapshotCorrelation ToSnapshot(MoodCorrelation c) => new()
    {
        Factor = c.FactorKey,
        Coefficient = Math.Round(c.Coefficient, 3),
        AdjustedPValue = Math.Round(c.AdjustedPValue, 4),
        SampleSize = c.SampleSize,
        LagDays = c.LagDays
    };
}
