using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Diarion.Models;

public partial class DiaryEntry : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();
    
    [ObservableProperty]
    private DateTime _date = DateTime.Today;
    
    // Сон (Sleep)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSleepStart))]
    [NotifyPropertyChangedFor(nameof(IsSleepStartEmpty))]
    private TimeSpan? _sleepStart;
    
    public bool HasSleepStart => SleepStart.HasValue;
    public bool IsSleepStartEmpty => !SleepStart.HasValue;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSleepEnd))]
    [NotifyPropertyChangedFor(nameof(IsSleepEndEmpty))]
    private TimeSpan? _sleepEnd;
    
    public bool HasSleepEnd => SleepEnd.HasValue;
    public bool IsSleepEndEmpty => !SleepEnd.HasValue;
    
    [ObservableProperty]
    private int _sleepQuality = 0;

    // Здоров'я / Фізіологія (Health & Physiology)
    [ObservableProperty]
    private string _cycleDay = string.Empty;
    
    [ObservableProperty]
    private int _healthStatus = 0;
    
    [ObservableProperty]
    private string _intimateLife = string.Empty;

    // Їжа (Food)
    [ObservableProperty]
    private bool _isBreakfastDone;
    
    [ObservableProperty]
    private string _breakfastFood = string.Empty;
    
    [ObservableProperty]
    private bool _isLunchDone;
    
    [ObservableProperty]
    private string _lunchFood = string.Empty;
    
    [ObservableProperty]
    private bool _isDinnerDone;
    
    [ObservableProperty]
    private string _dinnerFood = string.Empty;

    // Звички (Habits)
    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<HabitItem> _habits = new();

    // Шкала настрою (Mood Scale) - hours 7 to 23
    [ObservableProperty]
    private Dictionary<int, Emotion> _moodScale = new();

    // Рефлексія та Текстові блоки (Reflection & Text Blocks)
    [ObservableProperty]
    private string _triggers = string.Empty;
    
    [ObservableProperty]
    private string _gratitude = string.Empty;
    
    [ObservableProperty]
    private string _expenses = string.Empty;
    
    [ObservableProperty]
    private string _income = string.Empty;
    
    [ObservableProperty]
    private string _soulFood = string.Empty;
    
    [ObservableProperty]
    private string _supportForOthers = string.Empty;
    
    // Legacy fields (optional to keep for backward compatibility or general note)
    [ObservableProperty]
    private string _title = string.Empty;
    
    [ObservableProperty]
    private string _content = string.Empty;
    
    [ObservableProperty]
    private DateTime _createdAt = DateTime.Now;
    
    [ObservableProperty]
    private Emotion _emotion = Emotion.None;
    
    [ObservableProperty]
    private string _aiSummary = string.Empty;
}
