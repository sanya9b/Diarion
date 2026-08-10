using System;

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
