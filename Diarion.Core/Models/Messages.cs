using System;
using Diarion.Models;

namespace Diarion.Messages;

public class DateSelectedMessage
{
    public DateTime SelectedDate { get; }

    public DateSelectedMessage(DateTime selectedDate)
    {
        SelectedDate = selectedDate;
    }
}

/// <summary>
/// Asks the calendar to move to a date, as opposed to <see cref="DateSelectedMessage"/>, which
/// reports that it already has. Sent by search when a result is opened; the calendar answers by
/// selecting the day, which then raises DateSelectedMessage for everyone else.
/// </summary>
public class NavigateToDateMessage
{
    public DateTime Date { get; }

    public NavigateToDateMessage(DateTime date)
    {
        Date = date;
    }
}

/// <summary>
/// A diary entry or note was written or deleted. Announced by the services rather than by their
/// callers, so every save path is covered by one hook instead of one per view model — and sent as
/// a message rather than a method call because the indexer already depends on those services.
/// </summary>
public class DocumentChangedMessage
{
    public string SourceKind { get; }

    public string SourceId { get; }

    public DocumentChangedMessage(string sourceKind, string sourceId)
    {
        SourceKind = sourceKind;
        SourceId = sourceId;
    }
}

public class TodoChangedMessage
{
    public DateTime Date { get; }

    public TodoChangedMessage(DateTime date)
    {
        Date = date;
    }
}

/// <summary>Sent when the prompt library is edited, so screens holding a cached snapshot re-read it.</summary>
public class PromptLibraryChangedMessage
{
}

/// <summary>Sent when a period day is marked or unmarked, so the calendar repaints its cycle shading.</summary>
public class CycleLogChangedMessage
{
    public DateTime Date { get; }

    public CycleLogChangedMessage(DateTime date)
    {
        Date = date;
    }
}

/// <summary>Sent when the mood row's picture set is changed in settings, so the open day redraws its faces.</summary>
public class MoodIconSetChangedMessage
{
    public MoodIconSet Set { get; }

    public MoodIconSetChangedMessage(MoodIconSet set)
    {
        Set = set;
    }
}

public enum PaywallReason
{
    /// <summary>A write reached a service while the diary is read-only (spec 15, R5.2).</summary>
    WriteBlocked,
    /// <summary>The user asked: "Renew" on the read-only banner, "Subscribe" in settings.</summary>
    UserRequested,
    /// <summary>The first screen after onboarding for a new user (spec 15, R4.2).</summary>
    AfterOnboarding
}

/// <summary>
/// Asks the shell to show the paywall. The shell shows it at most once at a time — a blocked write
/// and a tap on "Renew" in the same moment open one sheet, not two — and only asks first whether
/// the user may already write for the automatic reasons: a 1.0 user who taps "Support with a
/// subscription" in Settings still gets the sheet.
/// </summary>
public sealed class PaywallRequestedMessage
{
    public PaywallReason Reason { get; }

    public PaywallRequestedMessage(PaywallReason reason)
    {
        Reason = reason;
    }
}

/// <summary>
/// Asks the statistics screen to open on a given window. Sent by the home-screen insight path after it
/// navigates there, so both screens count the same days (spec 17, R1.6).
/// </summary>
public sealed class StatisticsRangeRequestedMessage
{
    public StatsRange Range { get; }

    public StatisticsRangeRequestedMessage(StatsRange range)
    {
        Range = range;
    }
}

/// <summary>
/// Asks the widget coordinator to republish the snapshot for a reason the saves do not announce: the
/// widget privacy level or the language changed (spec 20).
/// </summary>
public sealed class WidgetRefreshRequestedMessage
{
}

/// <summary>
/// The app lock was switched on or off. Sent by the PIN screens; the widget coordinator answers to
/// "on" at once by dropping the chosen widget level and clearing the snapshot (spec 20, R9).
/// </summary>
public sealed class AppLockChangedMessage
{
    public bool IsEnabled { get; }

    public AppLockChangedMessage(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }
}

/// <summary>
/// A task, series or rule was written or deleted, announced by <c>TodoService</c> itself so every
/// save path is covered — the detail page saves without telling the planner, and the widget snapshot
/// must follow that save too (spec 20, R4). <see cref="TodoChangedMessage"/> stays the planner's own
/// word for "this day's list changed"; this one says only that the store did.
/// </summary>
public sealed class TodoStoreChangedMessage
{
}

public enum HomeSection
{
    Diary,
    Planner
}

/// <summary>
/// Asks the home screen to show one of its two halves. Sent by a widget deep link after it has
/// navigated to the home tab; the main view model switches the mode the way the tabs would.
/// </summary>
public sealed class HomeSectionRequestedMessage
{
    public HomeSection Section { get; }

    public HomeSectionRequestedMessage(HomeSection section)
    {
        Section = section;
    }
}
