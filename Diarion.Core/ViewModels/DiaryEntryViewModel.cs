using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Diarion.Models;
using Diarion.Services;

namespace Diarion.ViewModels;

public partial class DiaryEntryViewModel : ObservableObject
{
    public DiaryEntry Model { get; }

    public DiaryEntryViewModel(DiaryEntry model)
    {
        Model = model;
        _id = model.Id;
        _date = model.Date;
        _sleepStart = model.SleepStart;
        _sleepEnd = model.SleepEnd;
        _sleepQuality = model.SleepQuality;
        _sleepNotes = model.SleepNotes;
        _cycleDay = model.CycleDay;
        _healthStatus = model.HealthStatus;
        _isIntimateLifeDone = model.IsIntimateLifeDone;
        _intimateLife = model.IntimateLife;
        _isBreakfastDone = model.IsBreakfastDone;
        _breakfastFood = model.BreakfastFood;
        _isSecondBreakfastDone = model.IsSecondBreakfastDone;
        _secondBreakfastFood = model.SecondBreakfastFood;
        _isLunchDone = model.IsLunchDone;
        _lunchFood = model.LunchFood;
        _isSnackDone = model.IsSnackDone;
        _snackFood = model.SnackFood;
        _isDinnerDone = model.IsDinnerDone;
        _dinnerFood = model.DinnerFood;
        _triggers = model.Triggers;
        _gratitude = model.Gratitude;
        _soulFood = model.SoulFood;
        _supportForOthers = model.SupportForOthers;
        _promptResourceKey = model.PromptResourceKey;
        _promptAnswer = model.PromptAnswer;
        _title = model.Title;
        _content = model.Content;
        _createdAt = model.CreatedAt;
        _emotion = model.Emotion;
        _aiSummary = model.AiSummary;

        if (model.HabitsList != null)
        {
            foreach (var h in model.HabitsList)
            {
                Habits.Add(new HabitItemViewModel(h));
            }
        }

        Habits.CollectionChanged += (s, e) => UpdateModelHabits();

        BuildHourlyMood(model);
        RefreshPrompt();
    }

    private void UpdateModelHabits()
    {
        Model.HabitsList.Clear();
        foreach (var h in Habits)
        {
            h.SyncToModel();
            Model.HabitsList.Add(h.Model);
        }
    }

    // --- Hourly mood ---

    public const int FirstHour = MoodAggregate.FirstHour;
    public const int LastHour = MoodAggregate.LastHour;

    /// <summary>
    /// One slot per waking hour, always materialised so the grid has a stable shape. Slots left at
    /// <see cref="Emotion.None"/> are dropped on the way back to the model rather than stored.
    /// </summary>
    public ObservableCollection<HourMoodViewModel> HourlyMood { get; } = new();

    /// <summary>Which hour the emotion row writes to; null means the day-level scalar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMood))]
    [NotifyPropertyChangedFor(nameof(IsHourSelected))]
    private int? _selectedHour;

    public bool IsHourSelected => SelectedHour != null;

    /// <summary>
    /// What the emotion row should show as selected: the chosen hour's mood while an hour is picked,
    /// otherwise the day-level scalar. Without this the row would keep highlighting the day's mood
    /// while the user edits an hour.
    /// </summary>
    public Emotion CurrentMood => SelectedHour is int hour
        ? HourlyMood.FirstOrDefault(h => h.Hour == hour)?.Mood ?? Emotion.None
        : Emotion;

    /// <summary>Collapsed by default — the day screen already stacks several cards.</summary>
    [ObservableProperty]
    private bool _isHourlyExpanded;

    private void BuildHourlyMood(DiaryEntry model)
    {
        for (var hour = FirstHour; hour <= LastHour; hour++)
        {
            var stored = model.HourlyMood?.FirstOrDefault(h => h.Hour == hour);
            HourlyMood.Add(new HourMoodViewModel(hour, stored?.Mood ?? Emotion.None));
        }
    }

    private void UpdateModelHourlyMood()
    {
        Model.HourlyMood.Clear();
        foreach (var h in HourlyMood.Where(h => h.Mood != Emotion.None))
        {
            Model.HourlyMood.Add(new HourMood { Hour = h.Hour, Mood = h.Mood });
        }
    }

    [RelayCommand]
    private void ToggleHourly() => IsHourlyExpanded = !IsHourlyExpanded;

    [RelayCommand]
    private void SelectHour(HourMoodViewModel item)
    {
        if (item == null) return;

        // Tapping the selected hour again returns the emotion row to day level.
        SelectedHour = SelectedHour == item.Hour ? null : item.Hour;
        foreach (var h in HourlyMood) h.IsSelected = h.Hour == SelectedHour;
    }

    // Mood selection is 2-tap. With no hour selected it sets the day-level scalar, exactly as before;
    // with an hour selected it writes that hour instead. Either way PropertyChanged reaches the main
    // ViewModel, which triggers the debounced auto-save.
    [RelayCommand]
    private void SelectEmotion(Emotion emotion)
    {
        if (SelectedHour is not int hour)
        {
            Emotion = emotion;
            return;
        }

        var slot = HourlyMood.FirstOrDefault(h => h.Hour == hour);
        if (slot == null) return;

        // Re-picking the same emotion clears the hour — otherwise there would be no way to undo it.
        slot.Mood = slot.Mood == emotion ? Emotion.None : emotion;
        OnPropertyChanged(nameof(CurrentMood));
        UpdateModelHourlyMood();
        RefreshPrompt();
    }

    [ObservableProperty]
    private Guid _id;

    partial void OnIdChanged(Guid value) => Model.Id = value;

    [ObservableProperty]
    private DateTime _date;

    partial void OnDateChanged(DateTime value) => Model.Date = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSleepStart))]
    [NotifyPropertyChangedFor(nameof(IsSleepStartEmpty))]
    [NotifyPropertyChangedFor(nameof(SleepDurationText))]
    [NotifyPropertyChangedFor(nameof(SleepStartTime))]
    private TimeSpan? _sleepStart;

    partial void OnSleepStartChanged(TimeSpan? value) => Model.SleepStart = value;

    public bool HasSleepStart => SleepStart.HasValue;
    public bool IsSleepStartEmpty => !SleepStart.HasValue;

    public TimeSpan SleepStartTime
    {
        get => SleepStart ?? TimeSpan.Zero;
        set => SleepStart = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSleepEnd))]
    [NotifyPropertyChangedFor(nameof(IsSleepEndEmpty))]
    [NotifyPropertyChangedFor(nameof(SleepDurationText))]
    [NotifyPropertyChangedFor(nameof(SleepEndTime))]
    private TimeSpan? _sleepEnd;

    partial void OnSleepEndChanged(TimeSpan? value) => Model.SleepEnd = value;

    public bool HasSleepEnd => SleepEnd.HasValue;
    public bool IsSleepEndEmpty => !SleepEnd.HasValue;

    public TimeSpan SleepEndTime
    {
        get => SleepEnd ?? TimeSpan.Zero;
        set => SleepEnd = value;
    }

    public string SleepDurationText
    {
        get
        {
            if (SleepStart.HasValue && SleepEnd.HasValue)
            {
                var duration = SleepEnd.Value - SleepStart.Value;
                if (duration.TotalHours < 0)
                {
                    duration = duration.Add(TimeSpan.FromHours(24));
                }
                return $"{duration.Hours:00} {Diarion.Resources.Localization.AppResources.HoursShort} {duration.Minutes:00} {Diarion.Resources.Localization.AppResources.MinutesShort}";
            }
            return string.Empty;
        }
    }

    [ObservableProperty]
    private int _sleepQuality;

    partial void OnSleepQualityChanged(int value) => Model.SleepQuality = value;

    [ObservableProperty]
    private string _sleepNotes = string.Empty;

    partial void OnSleepNotesChanged(string value) => Model.SleepNotes = value;

    [ObservableProperty]
    private string _cycleDay = string.Empty;

    partial void OnCycleDayChanged(string value) => Model.CycleDay = value;

    [ObservableProperty]
    private int _healthStatus;

    partial void OnHealthStatusChanged(int value) => Model.HealthStatus = value;

    [ObservableProperty]
    private bool _isIntimateLifeDone;

    partial void OnIsIntimateLifeDoneChanged(bool value) => Model.IsIntimateLifeDone = value;

    [ObservableProperty]
    private string _intimateLife = string.Empty;

    partial void OnIntimateLifeChanged(string value) => Model.IntimateLife = value;

    [ObservableProperty]
    private bool _isBreakfastDone;

    partial void OnIsBreakfastDoneChanged(bool value) => Model.IsBreakfastDone = value;

    [ObservableProperty]
    private string _breakfastFood = string.Empty;

    partial void OnBreakfastFoodChanged(string value) => Model.BreakfastFood = value;

    [ObservableProperty]
    private bool _isSecondBreakfastDone;

    partial void OnIsSecondBreakfastDoneChanged(bool value) => Model.IsSecondBreakfastDone = value;

    [ObservableProperty]
    private string _secondBreakfastFood = string.Empty;

    partial void OnSecondBreakfastFoodChanged(string value) => Model.SecondBreakfastFood = value;

    [ObservableProperty]
    private bool _isLunchDone;

    partial void OnIsLunchDoneChanged(bool value) => Model.IsLunchDone = value;

    [ObservableProperty]
    private string _lunchFood = string.Empty;

    partial void OnLunchFoodChanged(string value) => Model.LunchFood = value;

    [ObservableProperty]
    private bool _isSnackDone;

    partial void OnIsSnackDoneChanged(bool value) => Model.IsSnackDone = value;

    [ObservableProperty]
    private string _snackFood = string.Empty;

    partial void OnSnackFoodChanged(string value) => Model.SnackFood = value;

    [ObservableProperty]
    private bool _isDinnerDone;

    partial void OnIsDinnerDoneChanged(bool value) => Model.IsDinnerDone = value;

    [ObservableProperty]
    private string _dinnerFood = string.Empty;

    partial void OnDinnerFoodChanged(string value) => Model.DinnerFood = value;

    [ObservableProperty]
    private string _triggers = string.Empty;

    partial void OnTriggersChanged(string value) => Model.Triggers = value;

    [ObservableProperty]
    private string _gratitude = string.Empty;

    partial void OnGratitudeChanged(string value) => Model.Gratitude = value;

    [ObservableProperty]
    private string _soulFood = string.Empty;

    partial void OnSoulFoodChanged(string value) => Model.SoulFood = value;

    [ObservableProperty]
    private string _supportForOthers = string.Empty;

    partial void OnSupportForOthersChanged(string value) => Model.SupportForOthers = value;

    [ObservableProperty]
    private string _promptResourceKey = string.Empty;

    partial void OnPromptResourceKeyChanged(string value)
    {
        Model.PromptResourceKey = value;
        OnPropertyChanged(nameof(PromptText));
    }

    [ObservableProperty]
    private string _promptAnswer = string.Empty;

    partial void OnPromptAnswerChanged(string value) => Model.PromptAnswer = value;

    /// <summary>The day's question, resolved from resources so it follows the UI language.</summary>
    public string PromptText => PromptCatalog.ResolveText(PromptResourceKey);

    /// <summary>
    /// Picks the question that fits the day's mood. Re-picks while the answer is still empty — the mood
    /// is usually recorded after the day screen first opens — but never once the user has written
    /// something, so the question they are answering cannot change under them.
    /// </summary>
    public void RefreshPrompt()
    {
        if (!string.IsNullOrWhiteSpace(PromptAnswer)) return;

        var wanted = PromptSelector.SelectCategory(Emotion, Model.HourlyMood, !string.IsNullOrWhiteSpace(Gratitude));
        if (PromptCatalog.CategoryOf(PromptResourceKey) == wanted) return;

        PromptResourceKey = PromptSelector.SelectKey(
            Date, Emotion, Model.HourlyMood, !string.IsNullOrWhiteSpace(Gratitude));
    }

    [RelayCommand]
    private void ShufflePrompt() => PromptResourceKey = PromptCatalog.Next(PromptResourceKey);

    [ObservableProperty]
    private string _title = string.Empty;

    partial void OnTitleChanged(string value) => Model.Title = value;

    [ObservableProperty]
    private string _content = string.Empty;

    partial void OnContentChanged(string value) => Model.Content = value;

    [ObservableProperty]
    private DateTime _createdAt;

    partial void OnCreatedAtChanged(DateTime value) => Model.CreatedAt = value;

    [ObservableProperty]
    private Emotion _emotion;

    partial void OnEmotionChanged(Emotion value)
    {
        Model.Emotion = value;
        OnPropertyChanged(nameof(CurrentMood));
        RefreshPrompt();
    }

    [ObservableProperty]
    private string _aiSummary = string.Empty;

    partial void OnAiSummaryChanged(string value) => Model.AiSummary = value;

    [ObservableProperty]
    private ObservableCollection<HabitItemViewModel> _habits = new();

    public void SyncToModel()
    {
        Model.Id = Id;
        Model.Date = Date;
        Model.SleepStart = SleepStart;
        Model.SleepEnd = SleepEnd;
        Model.SleepQuality = SleepQuality;
        Model.SleepNotes = SleepNotes;
        Model.CycleDay = CycleDay;
        Model.HealthStatus = HealthStatus;
        Model.IsIntimateLifeDone = IsIntimateLifeDone;
        Model.IntimateLife = IntimateLife;
        Model.IsBreakfastDone = IsBreakfastDone;
        Model.BreakfastFood = BreakfastFood;
        Model.IsSecondBreakfastDone = IsSecondBreakfastDone;
        Model.SecondBreakfastFood = SecondBreakfastFood;
        Model.IsLunchDone = IsLunchDone;
        Model.LunchFood = LunchFood;
        Model.IsSnackDone = IsSnackDone;
        Model.SnackFood = SnackFood;
        Model.IsDinnerDone = IsDinnerDone;
        Model.DinnerFood = DinnerFood;
        Model.Triggers = Triggers;
        Model.Gratitude = Gratitude;
        Model.SoulFood = SoulFood;
        Model.PromptResourceKey = PromptResourceKey;
        Model.PromptAnswer = PromptAnswer;
        Model.SupportForOthers = SupportForOthers;
        Model.Title = Title;
        Model.Content = Content;
        Model.CreatedAt = CreatedAt;
        Model.Emotion = Emotion;
        Model.AiSummary = AiSummary;

        UpdateModelHabits();
        UpdateModelHourlyMood();
    }
}

/// <summary>One hour slot in the mood grid.</summary>
public partial class HourMoodViewModel : ObservableObject
{
    public HourMoodViewModel(int hour, Emotion mood)
    {
        Hour = hour;
        _mood = mood;
    }

    public int Hour { get; }

    public string HourLabel => Hour.ToString("00");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorHex))]
    [NotifyPropertyChangedFor(nameof(HasMood))]
    [NotifyPropertyChangedFor(nameof(AccessibilityText))]
    private Emotion _mood;

    /// <summary>Selected in the grid, so the emotion row writes here instead of the day.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Transparent while unset; MAUI parses the hex straight into BackgroundColor.</summary>
    public string ColorHex => Mood == Emotion.None ? "#00000000" : Mood.ToColorHex();

    public bool HasMood => Mood != Emotion.None;

    public string AccessibilityText => string.Format(
        Diarion.Resources.Localization.AppResources.MoodHourAccessibility,
        Hour,
        Mood == Emotion.None
            ? Diarion.Resources.Localization.AppResources.MoodHourEmpty
            : Mood.ToLocalizedName());
}
