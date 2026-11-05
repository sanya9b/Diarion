using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Widgets;
using Diarion.Resources.Localization;

namespace Diarion.Services.Widgets;

/// <inheritdoc />
/// <remarks>
/// Reads through the same services the home screen reads through, so a widget can never disagree
/// with the page behind it: the streak is <see cref="IDiaryService.GetCurrentStreakAsync"/>, the
/// habits are the day's scheduled definitions against the entry's habit list, the tasks are the
/// planner's own day query. Nothing is cached here; the coordinator decides when to ask.
/// </remarks>
public sealed class WidgetSnapshotService : IWidgetSnapshotService
{
    /// <summary>More rows than any widget size can draw; the file stays small whatever the planner holds.</summary>
    public const int MaxTaskItems = 8;
    public const int MaxHabitItems = 10;

    private readonly IDiaryService _diaryService;
    private readonly ITodoService _todoService;
    private readonly IHabitService _habitService;
    private readonly TimeProvider _clock;

    public WidgetSnapshotService(
        IDiaryService diaryService,
        ITodoService todoService,
        IHabitService habitService,
        TimeProvider? clock = null)
    {
        _diaryService = diaryService;
        _todoService = todoService;
        _habitService = habitService;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<WidgetSnapshot> BuildAsync(DateTime today, WidgetPrivacyLevel level)
    {
        var day = today.Date;
        var culture = AppResources.Culture ?? CultureInfo.CurrentUICulture;
        var now = _clock.GetLocalNow();

        var labels = new WidgetLabels
        {
            AppName = AppResources.AppTitle,
            Open = AppResources.WidgetOpenLabel,
            DateShort = day.ToString("ddd, d MMM", culture),
            Updated = string.Format(culture, AppResources.WidgetUpdatedFormat, now.ToString("t", culture))
        };

        var snapshot = new WidgetSnapshot
        {
            Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            GeneratedAt = now,
            Locale = culture.TwoLetterISOLanguageName,
            PrivacyLevel = level,
            Labels = labels
        };

        if (level == WidgetPrivacyLevel.ButtonOnly) return snapshot;

        var withText = level == WidgetPrivacyLevel.WithText;
        var entry = await _diaryService.GetEntryForDateAsync(day);
        var streak = await _diaryService.GetCurrentStreakAsync();
        var todos = await _todoService.GetTodosForDateAsync(day);
        var habits = await _habitService.GetActiveHabitsForDateAsync(day);

        return snapshot with
        {
            Streak = BuildStreak(streak, culture),
            Mood = BuildMood(entry),
            Tasks = BuildTasks(todos, withText, culture),
            Habits = BuildHabits(habits, entry, withText, culture)
        };
    }

    private static WidgetStreak BuildStreak(StreakResult streak, CultureInfo culture)
    {
        var label = streak.Length == 0
            ? AppResources.WidgetStreakNone
            : string.Format(culture, PluralDays(streak.Length), streak.Length);

        return new WidgetStreak { Days = streak.Length, HeldByGrace = streak.HeldByGrace, Label = label };
    }

    private static WidgetMood BuildMood(DiaryEntry entry)
    {
        var quadrant = MoodAggregate.DominantQuadrant(entry.Emotion, entry.HourlyMood);
        return quadrant == EmotionQuadrant.None
            ? new WidgetMood { Quadrant = null, Label = AppResources.WidgetMoodEmpty }
            : new WidgetMood { Quadrant = quadrant.ToString(), Label = AppResources.WidgetMoodLabel };
    }

    private static WidgetTasks BuildTasks(List<TodoItem> todos, bool withText, CultureInfo culture)
    {
        var done = todos.Count(t => t.IsCompleted);
        var label = todos.Count == 0
            ? AppResources.WidgetTasksNone
            : string.Format(culture, AppResources.WidgetTasksFormat, done, todos.Count);

        IReadOnlyList<WidgetTaskItem>? items = null;
        if (withText)
        {
            // Open tasks first, timed before all-day, the way the planner lists them; done ones sink
            // to the bottom so a widget that shows three rows shows what is still to do.
            items = todos
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.HasTime)
                .ThenBy(t => t.HasTime ? t.TargetTime : TimeSpan.MaxValue)
                .ThenBy(t => t.CreatedAt)
                .Take(MaxTaskItems)
                .Select(t => new WidgetTaskItem
                {
                    Title = t.TaskDescription,
                    Time = t.HasTime ? DateTime.Today.Add(t.TargetTime).ToString("t", culture) : null,
                    Done = t.IsCompleted,
                    Priority = t.Priority.ToString()
                })
                .ToList();
        }

        return new WidgetTasks { Done = done, Total = todos.Count, Label = label, Items = items };
    }

    private static WidgetHabits BuildHabits(
        List<HabitDefinition> definitions, DiaryEntry entry, bool withText, CultureInfo culture)
    {
        var completed = entry.HabitsList
            .Where(h => h.IsCompleted)
            .Select(h => h.HabitId)
            .ToHashSet();

        var ordered = definitions
            .OrderBy(d => d.Order)
            .ThenBy(d => d.CreatedAt)
            .ToList();

        var done = ordered.Count(d => completed.Contains(d.Id));
        var label = ordered.Count == 0
            ? AppResources.WidgetHabitsNone
            : string.Format(culture, AppResources.WidgetHabitsFormat, done, ordered.Count);

        IReadOnlyList<WidgetHabitItem>? items = null;
        if (withText)
        {
            items = ordered
                .Take(MaxHabitItems)
                .Select(d => new WidgetHabitItem { Name = d.Name, Done = completed.Contains(d.Id) })
                .ToList();
        }

        return new WidgetHabits { Done = done, Total = ordered.Count, Label = label, Items = items };
    }

    /// <summary>Ukrainian needs three forms (1 день, 3 дні, 14 днів); English carries the same three keys.</summary>
    private static string PluralDays(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        return mod10 == 1 && mod100 != 11 ? AppResources.WidgetStreakDayOne
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? AppResources.WidgetStreakDayFew
            : AppResources.WidgetStreakDayMany;
    }
}
