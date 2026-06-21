using System;
using System.Collections.Generic;

namespace Diarion.Models;

public class DiaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public DateTime Date { get; set; } = DateTime.Today;
    
    // Сон (Sleep)
    public TimeSpan? SleepStart { get; set; }
    
    public TimeSpan? SleepEnd { get; set; }
    
    public int SleepQuality { get; set; } = 0;

    // Здоров'я / Фізіологія (Health & Physiology)
    public string CycleDay { get; set; } = string.Empty;
    
    public int HealthStatus { get; set; } = 0;
    
    public string IntimateLife { get; set; } = string.Empty;

    // Їжа (Food)
    public bool IsBreakfastDone { get; set; }
    
    public string BreakfastFood { get; set; } = string.Empty;
    
    public bool IsLunchDone { get; set; }
    
    public string LunchFood { get; set; } = string.Empty;
    
    public bool IsDinnerDone { get; set; }
    
    public string DinnerFood { get; set; } = string.Empty;

    // Звички (Habits)
    // List used for DB persistence
    public List<HabitItem> HabitsList { get; set; } = new();

    // Шкала настрою (Mood Scale) - hours 7 to 23
    public Dictionary<int, Emotion> MoodScale { get; set; } = new();

    // Рефлексія та Текстові блоки (Reflection & Text Blocks)
    public string Triggers { get; set; } = string.Empty;
    
    public string Gratitude { get; set; } = string.Empty;
    
    public string SoulFood { get; set; } = string.Empty;
    
    public string SupportForOthers { get; set; } = string.Empty;
    
    // Legacy fields (optional to keep for backward compatibility or general note)
    public string Title { get; set; } = string.Empty;
    
    public string Content { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public Emotion Emotion { get; set; } = Emotion.None;
    
    public string AiSummary { get; set; } = string.Empty;
}
