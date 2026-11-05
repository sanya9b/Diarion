using System;
using System.Collections.Generic;

namespace Diarion.Models.Widgets;

/// <summary>
/// Everything a home-screen widget is allowed to draw, written by the app as one small JSON file and
/// read by the widget process on both platforms (spec 20, R1). The shape is a contract with code that
/// is not C# — the iOS widget decodes it in Swift — so: additive changes only, bump
/// <see cref="CurrentSchemaVersion"/> when a reader could misread an old file, and keep the labels in
/// here already localised, so the widgets carry no dictionary of their own (D4).
/// <para>
/// Sections are null when the privacy level does not allow them. A reader that finds a null section
/// draws its placeholder; a reader that finds every section null is looking at the button-only level.
/// </para>
/// </summary>
public sealed record WidgetSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The day the counts are about, as yyyy-MM-dd. A widget that wakes up on a later day
    /// knows the numbers are stale and says so rather than showing yesterday as today.</summary>
    public required string Date { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Two-letter language the labels are in ("uk", "en").</summary>
    public required string Locale { get; init; }

    public required WidgetPrivacyLevel PrivacyLevel { get; init; }

    public required WidgetLabels Labels { get; init; }

    public WidgetStreak? Streak { get; init; }

    public WidgetMood? Mood { get; init; }

    public WidgetTasks? Tasks { get; init; }

    public WidgetHabits? Habits { get; init; }
}

/// <summary>Strings every level carries, so even the button-only widget reads in the app's language.</summary>
public sealed record WidgetLabels
{
    public required string AppName { get; init; }

    /// <summary>"Open diary" — the button on the button-only widget and the fallback tap hint elsewhere.</summary>
    public required string Open { get; init; }

    /// <summary>The day in the user's own calendar words ("Wed, 5 Nov").</summary>
    public required string DateShort { get; init; }

    /// <summary>"Updated 14:02" — honest about how fresh the numbers are, since iOS throttles refreshes.</summary>
    public required string Updated { get; init; }
}

public sealed record WidgetStreak
{
    public required int Days { get; init; }

    public required bool HeldByGrace { get; init; }

    /// <summary>"12 days in a row" in the right plural form, or the nudge to start one.</summary>
    public required string Label { get; init; }
}

public sealed record WidgetMood
{
    /// <summary>An <see cref="EmotionQuadrant"/> name, or null when nothing is recorded for the day.</summary>
    public string? Quadrant { get; init; }

    public required string Label { get; init; }
}

public sealed record WidgetTasks
{
    public required int Done { get; init; }

    public required int Total { get; init; }

    /// <summary>"2 of 5 tasks" or "No tasks today".</summary>
    public required string Label { get; init; }

    /// <summary>Only at <see cref="WidgetPrivacyLevel.WithText"/>; null otherwise.</summary>
    public IReadOnlyList<WidgetTaskItem>? Items { get; init; }
}

public sealed record WidgetTaskItem
{
    public required string Title { get; init; }

    /// <summary>"14:00" for a timed task, null for an all-day one.</summary>
    public string? Time { get; init; }

    public required bool Done { get; init; }

    /// <summary>A <see cref="TodoPriority"/> name, so the widget can colour the dot the way the planner does.</summary>
    public required string Priority { get; init; }
}

public sealed record WidgetHabits
{
    public required int Done { get; init; }

    public required int Total { get; init; }

    /// <summary>"3 of 4 habits" or "No habits today".</summary>
    public required string Label { get; init; }

    /// <summary>Only at <see cref="WidgetPrivacyLevel.WithText"/>; null otherwise.</summary>
    public IReadOnlyList<WidgetHabitItem>? Items { get; init; }
}

public sealed record WidgetHabitItem
{
    public required string Name { get; init; }

    public required bool Done { get; init; }
}
