using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;

namespace Diarion.ViewModels;

public partial class TodoItemViewModel : ObservableObject
{
    public TodoItem Model { get; }

    private readonly Action<TodoItemViewModel>? _onDelete;

    /// <param name="isRecurring">
    /// Whether the series this row came from is still running. Not <c>RecurringTaskId != null</c>: an
    /// occurrence keeps pointing at its rule after the series ends, because that provenance is what pins
    /// it against auto-migration. Reading the glyph off the id meant a row went on advertising itself as
    /// repeating after the user had switched the repeat off — which looks exactly like the switch not
    /// having worked.
    /// </param>
    public TodoItemViewModel(TodoItem model, bool isRecurring = false, Action<TodoItemViewModel>? onDelete = null)
    {
        Model = model;
        _onDelete = onDelete;
        _id = model.Id;
        _targetDate = model.TargetDate;
        _hasTime = model.HasTime;
        _targetTime = model.TargetTime;
        _endTime = model.EndTime;
        _taskDescription = model.TaskDescription;
        _isCompleted = model.IsCompleted;
        _isRecurring = isRecurring;
        _hasReminder = model.HasReminder;
        _priority = model.Priority;
    }

    /// <summary>
    /// Deleting is a command on the row itself rather than a binding that has to go looking for the
    /// section. The swipe button lives outside the row's visual tree, so neither <c>x:Reference</c> nor an
    /// ancestor lookup reaches it from there — the button bound to nothing and silently did nothing.
    /// </summary>
    [RelayCommand]
    private void Delete() => _onDelete?.Invoke(this);

    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private DateTime _targetDate;

    [ObservableProperty]
    private bool _hasTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeDisplay))]
    private TimeSpan _targetTime;

    /// <summary>Where the block stops, exclusive, or null for a point task.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEndTime))]
    [NotifyPropertyChangedFor(nameof(TimeDisplay))]
    private TimeSpan? _endTime;

    public bool HasEndTime => EndTime != null;

    /// <summary>
    /// The time as the row prints it: "13:00", or "13:00–16:00" once the task owns a stretch of the day.
    /// Composed here rather than by a XAML converter for the reason the task form composes its own —
    /// Windows draws a TimePicker as two native spin fields that ignore Format, so the app only agrees
    /// with itself across platforms when it writes the text out itself.
    /// </summary>
    public string TimeDisplay => EndTime == null
        ? TargetTime.ToString(@"hh\:mm")
        : $"{TargetTime:hh\\:mm}–{EndTime.Value:hh\\:mm}";

    /// <summary>
    /// What a continuation strip says to a screen reader. The strip itself shows only the task's name and
    /// the word "ongoing", which out of the visual context of the hour column says nothing about when the
    /// block started or when it lets go.
    /// </summary>
    public string SpanAccessibility => string.Format(
        Diarion.Resources.Localization.AppResources.TaskSpanAccessibility,
        TaskDescription,
        TargetTime.ToString(@"hh\:mm"),
        (EndTime ?? TargetTime).ToString(@"hh\:mm"));

    [ObservableProperty]
    private string _taskDescription = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    partial void OnIsCompletedChanged(bool value)
    {
        Model.IsCompleted = value;
    }

    /// <summary>Read-only on a row: the series is edited through the task form, never from the list.</summary>
    [ObservableProperty]
    private bool _isRecurring;

    [ObservableProperty]
    private bool _hasReminder;

    [ObservableProperty]
    private TodoPriority _priority;

    public void SyncToModel()
    {
        Model.Id = Id;
        Model.TargetDate = TargetDate;
        Model.HasTime = HasTime;
        Model.TargetTime = TargetTime;
        Model.EndTime = EndTime;
        Model.TaskDescription = TaskDescription;
        Model.IsCompleted = IsCompleted;
        Model.HasReminder = HasReminder;
        Model.Priority = Priority;
    }
}
