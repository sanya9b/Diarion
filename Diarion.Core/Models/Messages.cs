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
