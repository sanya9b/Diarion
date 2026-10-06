using System;
using System.Collections.Generic;
using System.Linq;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Works out which repeating tasks should exist on one given day. Pure and deterministic — the day is
/// passed in and nothing is read or written — so the guards below can be tested directly.
///
/// One day at a time, not a window. The planner screen is random access: the user walks days in both
/// directions, so there is no "how far have we got" to remember and nothing to cap. Two rows for the same
/// occurrence are prevented by the only key that means anything, <c>(rule, date)</c>, which is idempotent
/// without storing any state at all.
/// </summary>
public static class RecurringTaskPlanner
{
    /// <summary>
    /// How many uncompleted High-priority tasks a day may hold before an arriving one is demoted. Three is
    /// what the hand-written generator used; naming it keeps the auto-migration path and this one agreeing.
    /// </summary>
    public const int MaxHighPriorityPerDay = 3;

    /// <summary>
    /// The occurrences missing from <paramref name="existingForDay"/>, as unpersisted rows. The caller
    /// inserts them; nothing here writes.
    /// </summary>
    public static List<TodoItem> PlanForDay(
        IEnumerable<RecurringTask> rules,
        IEnumerable<TodoItem> existingForDay,
        DateTime day)
    {
        var date = day.Date;
        var planned = new List<TodoItem>();

        var onTheDay = (existingForDay ?? Enumerable.Empty<TodoItem>()).Where(t => t != null).ToList();

        // Exact, by rule id. The scheme this replaces fell back to matching on the task's own text, which
        // is what merged two unrelated tasks that happened to read the same into one series.
        var alreadyMaterialized = onTheDay
            .Where(t => t.RecurringTaskId != null)
            .Select(t => t.RecurringTaskId!.Value)
            .ToHashSet();

        var highCount = onTheDay.Count(t => t.Priority == TodoPriority.High && !t.IsCompleted);

        // Ordered so that when two rules compete for the last High slot, the same one loses every time.
        // Left to LiteDB's return order the demotion would be arbitrary, and arbitrary in a way no test
        // would catch.
        var ordered = (rules ?? Enumerable.Empty<RecurringTask>())
            .Where(r => r != null)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id);

        foreach (var rule in ordered)
        {
            if (alreadyMaterialized.Contains(rule.Id)) continue;
            if (rule.IsSkipped(date)) continue;
            if (!(rule.Recurrence ?? new RecurrenceRule()).IsOccurrenceOn(date)) continue;

            var priority = rule.Priority;
            if (priority == TodoPriority.High && highCount >= MaxHighPriorityPerDay)
            {
                priority = TodoPriority.Medium;
            }
            if (priority == TodoPriority.High) highCount++;

            planned.Add(new TodoItem
            {
                Id = Guid.NewGuid(),
                RecurringTaskId = rule.Id,
                TargetDate = date,
                TaskDescription = rule.TaskDescription,
                Priority = priority,
                HasTime = rule.HasTime,
                TargetTime = rule.TargetTime,
                EndTime = rule.EndTime,
                HasReminder = rule.HasReminder,
                IsCompleted = false,
                CreatedAt = DateTime.Now
            });
        }

        return planned;
    }
}
