using System.Linq;

namespace Diarion.Models;

public static class DiaryEntryExtensions
{
    /// <summary>
    /// Whether the user actually put something in this day, as opposed to merely opening it. Rows are
    /// created as a side effect of browsing — the day screen writes <see cref="DiaryEntry.CycleDay"/>
    /// whenever cycle tracking is on — so "a row exists for this date" is not the same as "journaled".
    /// <see cref="DiaryEntry.CycleDay"/> and the identity fields are therefore deliberately
    /// excluded: none of them is something the user typed.
    /// </summary>
    public static bool HasContent(this DiaryEntry? entry)
    {
        if (entry is null) return false;

        return entry.Emotion != Emotion.None
            || entry.HourlyMood.Any(h => h.Mood != Emotion.None)
            || HasText(entry.Triggers)
            || HasText(entry.Gratitude)
            || HasText(entry.SoulFood)
            || HasText(entry.SupportForOthers)
            || HasText(entry.PromptAnswer)
            || HasText(entry.Title)
            || HasText(entry.Content)
            || HasText(entry.SleepNotes)
            || HasText(entry.IntimateLife)
            || entry.IsIntimateLifeDone
            || entry.SleepStart.HasValue
            || entry.SleepEnd.HasValue
            || entry.SleepQuality > 0
            || entry.HealthStatus > 0
            || HasAnyMeal(entry)
            || entry.HabitsList.Any(h => h.IsCompleted);
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasAnyMeal(DiaryEntry e) =>
        e.IsBreakfastDone || HasText(e.BreakfastFood)
        || e.IsSecondBreakfastDone || HasText(e.SecondBreakfastFood)
        || e.IsLunchDone || HasText(e.LunchFood)
        || e.IsSnackDone || HasText(e.SnackFood)
        || e.IsDinnerDone || HasText(e.DinnerFood);
}
