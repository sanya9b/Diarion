using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Diarion.Messages;
using Diarion.Models;
using Diarion.Resources.Localization;
using Diarion.Services;
using Diarion.Services.Store;

namespace Diarion.ViewModels;

/// <summary>
/// The ten-second entry: how you feel right now, one line, and which habits are done — into
/// today's row, from one sheet, without the day screen.
/// </summary>
/// <remarks>
/// <para>
/// It writes exactly what the day screen would have written, into the same fields, so nothing here
/// is a second kind of entry. The mood goes to the current hour of the hourly scale (and to the
/// day's own mood only if that is still blank — a summary the user set on the day screen is theirs);
/// the line is appended to "for myself", the one free field the day has; the habit ticks are the
/// same ticks. A day that already has a row keeps everything else in it untouched.
/// </para>
/// <para>
/// Nothing is written until Save, and Save is disabled until something has changed. The sheet can
/// be opened from a reminder or a home-screen shortcut, which means it is opened idly as often as
/// on purpose, and an idle opening must not leave a blank row behind — the defect the home screen
/// spent a specification getting rid of.
/// </para>
/// </remarks>
public partial class QuickCaptureViewModel : BaseViewModel
{
    /// <summary>The Shell route the sheet is registered on.</summary>
    public const string Route = "QuickCapture";

    private readonly IDiaryService _diaryService;
    private readonly IDiaryHabitSyncService _habitSync;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _clock;

    private DiaryEntry? _entry;

    /// <summary>What the hour held when the sheet opened; "changed" is measured against this.</summary>
    private Emotion _initialEmotion;

    [ObservableProperty]
    private DateTime _date;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HourText))]
    private int _hour;

    public string HourText => string.Format(AppResources.QuickCaptureHourFormat, Hour);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(SelectedMoodName))]
    [NotifyPropertyChangedFor(nameof(HasSelectedMood))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private Emotion _selectedEmotion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _hasHabits;

    /// <summary>
    /// The day screen's row of faces, in its order — unpleasant on the left, pleasant on the right —
    /// built from the same view model, so the sheet shows the same faces in the same hues and follows
    /// the picker whenever it changes.
    /// </summary>
    public IReadOnlyList<MoodQuadrantViewModel> Quadrants { get; } = new[]
    {
        new MoodQuadrantViewModel(EmotionQuadrant.UnpleasantCalm, column: 0),
        new MoodQuadrantViewModel(EmotionQuadrant.UnpleasantEnergetic, column: 1),
        new MoodQuadrantViewModel(EmotionQuadrant.Neutral, column: 2),
        new MoodQuadrantViewModel(EmotionQuadrant.PleasantCalm, column: 3),
        new MoodQuadrantViewModel(EmotionQuadrant.PleasantEnergetic, column: 4)
    };

    /// <summary>
    /// The precise states of the chosen face, representative first. Inline under the row rather than
    /// in a bubble: the sheet is short, and the ten-second promise is that a face alone is an entry —
    /// the chips are there for whoever wants the word, not a second step everyone must take.
    /// </summary>
    public ObservableCollection<MoodChipViewModel> Refinements { get; } = new();

    [ObservableProperty]
    private bool _hasRefinements;

    /// <summary>The chosen state's name, under the row, the way the day screen names it.</summary>
    public string SelectedMoodName => SelectedEmotion == Emotion.None ? string.Empty : SelectedEmotion.ToLocalizedName();

    public bool HasSelectedMood => SelectedEmotion != Emotion.None;

    public ObservableCollection<QuickHabitChip> Habits { get; } = new();

    public bool CanSave =>
        IsLoaded
        && !IsBusy
        && (SelectedEmotion != _initialEmotion
            || !string.IsNullOrWhiteSpace(Text)
            || Habits.Any(h => h.IsChanged));

    public QuickCaptureViewModel(
        IDiaryService diaryService,
        IDiaryHabitSyncService habitSync,
        INavigationService navigation,
        TimeProvider? clock = null)
    {
        _diaryService = diaryService;
        _habitSync = habitSync;
        _navigation = navigation;
        _clock = clock ?? TimeProvider.System;
        Title = AppResources.QuickCaptureTitle;
    }

    protected override void OnBusyStateChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        var now = _clock.GetLocalNow().DateTime;
        Date = now.Date;

        // The scale runs 7..23. Before seven the night is still "yesterday evening" to most people,
        // but the row is today's, so the mood lands on the first hour rather than being dropped.
        Hour = Math.Clamp(now.Hour, MoodAggregate.FirstHour, MoodAggregate.LastHour);

        var entry = await _diaryService.GetEntryForDateAsync(Date);
        await _habitSync.SyncHabitsForEntryAsync(entry);
        _entry = entry;

        _initialEmotion = entry.HourlyMood.FirstOrDefault(h => h.Hour == Hour)?.Mood ?? Emotion.None;
        SelectedEmotion = _initialEmotion;

        foreach (var chip in Habits)
        {
            chip.PropertyChanged -= OnChipChanged;
        }

        Habits.Clear();
        foreach (var habit in entry.HabitsList)
        {
            var chip = new QuickHabitChip(habit.HabitId, habit.Name, habit.IsCompleted);
            chip.PropertyChanged += OnChipChanged;
            Habits.Add(chip);
        }

        HasHabits = Habits.Count > 0;
        IsLoaded = true;
    }

    private void OnChipChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QuickHabitChip.IsCompleted)) return;

        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedEmotionChanged(Emotion value)
    {
        var quadrant = value.ToQuadrant();
        foreach (var face in Quadrants)
        {
            face.IsSelected = value != Emotion.None && face.Quadrant == quadrant;
        }

        RebuildRefinements(quadrant);
        foreach (var chip in Refinements)
        {
            chip.IsSelected = chip.Emotion == value;
        }
    }

    /// <summary>Rebuilt only when the quadrant changes, so tapping a chip does not redraw its row.</summary>
    private void RebuildRefinements(EmotionQuadrant quadrant)
    {
        if (Refinements.Count > 0 && Refinements[0].Emotion.ToQuadrant() == quadrant) return;

        Refinements.Clear();
        if (quadrant != EmotionQuadrant.None)
        {
            var members = quadrant.Emotions();
            // A quadrant of one (neutral) has nothing to refine to.
            if (members.Count > 1)
            {
                foreach (var emotion in members)
                {
                    Refinements.Add(new MoodChipViewModel(emotion));
                }
            }
        }

        HasRefinements = Refinements.Count > 0;
    }

    /// <summary>
    /// A face records its representative, as on the day screen. Tapping the chosen face again keeps the
    /// precise state already picked inside it — a face is never an "off" switch here either; clearing
    /// is its own, deliberate action.
    /// </summary>
    [RelayCommand]
    private void SelectQuadrant(EmotionQuadrant quadrant)
    {
        if (quadrant == EmotionQuadrant.None) return;
        if (SelectedEmotion != Emotion.None && SelectedEmotion.ToQuadrant() == quadrant) return;

        SelectedEmotion = quadrant.Representative();
    }

    [RelayCommand]
    private void SelectEmotion(Emotion emotion) => SelectedEmotion = emotion;

    [RelayCommand]
    private void ClearMood() => SelectedEmotion = Emotion.None;

    [RelayCommand]
    private void ToggleHabit(QuickHabitChip? chip)
    {
        if (chip is null) return;
        chip.IsCompleted = !chip.IsCompleted;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_entry is null || !CanSave) return;

        var entry = _entry;
        Apply(entry);

        IsBusy = true;
        try
        {
            // Read-only mode throws out of the service; the gate turns that into the paywall and a
            // false, and the sheet stays open with everything the user typed still in it.
            if (!await WriteGate.TryAsync(() => _diaryService.SaveEntryAsync(entry))) return;
        }
        finally
        {
            IsBusy = false;
        }

        WeakReferenceMessenger.Default.Send(new QuickCaptureSavedMessage(Date));
        await _navigation.NavigateBackAsync();
    }

    private void Apply(DiaryEntry entry)
    {
        var slot = entry.HourlyMood.FirstOrDefault(h => h.Hour == Hour);
        if (SelectedEmotion == Emotion.None)
        {
            if (slot is not null) entry.HourlyMood.Remove(slot);
        }
        else if (slot is not null)
        {
            slot.Mood = SelectedEmotion;
        }
        else
        {
            entry.HourlyMood.Add(new HourMood { Hour = Hour, Mood = SelectedEmotion });
        }

        // Fill a blank day-level mood, never replace one: that scalar is the user's own summary of
        // the day, set on the day screen, and an hour's reading does not outrank it.
        if (entry.Emotion == Emotion.None && SelectedEmotion != Emotion.None)
        {
            entry.Emotion = SelectedEmotion;
        }

        var line = Text.Trim();
        if (line.Length > 0)
        {
            entry.SoulFood = string.IsNullOrWhiteSpace(entry.SoulFood)
                ? line
                : entry.SoulFood.TrimEnd() + Environment.NewLine + line;
        }

        foreach (var chip in Habits)
        {
            var item = entry.HabitsList.FirstOrDefault(h => h.HabitId == chip.HabitId);
            if (item is not null) item.IsCompleted = chip.IsCompleted;
        }
    }

    [RelayCommand]
    private Task CancelAsync() => _navigation.NavigateBackAsync();

    /// <summary>
    /// The way out to the full form. The home screen is told which day to show first, because the
    /// sheet is about today and the calendar underneath may be standing on another day.
    /// </summary>
    [RelayCommand]
    private async Task OpenFullEntryAsync()
    {
        WeakReferenceMessenger.Default.Send(new NavigateToDateMessage(Date));
        await _navigation.NavigateBackAsync();
    }
}

/// <summary>One habit chip: the tick as it stands, and whether it differs from how the sheet found it.</summary>
public partial class QuickHabitChip : ObservableObject
{
    public QuickHabitChip(Guid habitId, string name, bool wasCompleted)
    {
        HabitId = habitId;
        Name = name;
        WasCompleted = wasCompleted;
        _isCompleted = wasCompleted;
    }

    public Guid HabitId { get; }

    public string Name { get; }

    public bool WasCompleted { get; }

    /// <summary>The theme key a ticked chip fills with — the same accent as the home block's ticks.</summary>
    public const string TickedColorKey = "Theme_Coral";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyPropertyChangedFor(nameof(FillColorKey))]
    private bool _isCompleted;

    public bool IsChanged => IsCompleted != WasCompleted;

    /// <summary>
    /// The chip's fill layer: a theme key while ticked, null (see-through) otherwise. A value rather
    /// than a trigger, and a key rather than a colour, for the reasons in <see cref="MoodQuadrantViewModel"/>.
    /// </summary>
    public string? FillColorKey => IsCompleted ? TickedColorKey : null;
}
