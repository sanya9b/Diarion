using System;

namespace Diarion.Models;

public class DiaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Title { get; set; } = string.Empty;
    
    public string Content { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public Emotion Emotion { get; set; } = Emotion.None;
    
    public string AiSummary { get; set; } = string.Empty;
}
