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
