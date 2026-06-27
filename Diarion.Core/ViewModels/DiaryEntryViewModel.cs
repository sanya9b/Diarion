using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;

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
        _cycleDay = model.CycleDay;
        _healthStatus = model.HealthStatus;
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
    private TimeSpan? _sleepStart;

    partial void OnSleepStartChanged(TimeSpan? value) => Model.SleepStart = value;

    public bool HasSleepStart => SleepStart.HasValue;
    public bool IsSleepStartEmpty => !SleepStart.HasValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSleepEnd))]
    [NotifyPropertyChangedFor(nameof(IsSleepEndEmpty))]
    [NotifyPropertyChangedFor(nameof(SleepDurationText))]
    private TimeSpan? _sleepEnd;

    partial void OnSleepEndChanged(TimeSpan? value) => Model.SleepEnd = value;

    public bool HasSleepEnd => SleepEnd.HasValue;
    public bool IsSleepEndEmpty => !SleepEnd.HasValue;

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
                return $"{duration.Hours:00} год {duration.Minutes:00} хв";
            }
            return string.Empty;
        }
    }

    [ObservableProperty]
    private int _sleepQuality;

    partial void OnSleepQualityChanged(int value) => Model.SleepQuality = value;

    [ObservableProperty]
    private string _cycleDay = string.Empty;

    partial void OnCycleDayChanged(string value) => Model.CycleDay = value;

    [ObservableProperty]
    private int _healthStatus;

    partial void OnHealthStatusChanged(int value) => Model.HealthStatus = value;

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

    partial void OnEmotionChanged(Emotion value) => Model.Emotion = value;

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
        Model.CycleDay = CycleDay;
        Model.HealthStatus = HealthStatus;
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
        Model.SupportForOthers = SupportForOthers;
        Model.Title = Title;
        Model.Content = Content;
        Model.CreatedAt = CreatedAt;
        Model.Emotion = Emotion;
        Model.AiSummary = AiSummary;
        
        UpdateModelHabits();
    }
}
