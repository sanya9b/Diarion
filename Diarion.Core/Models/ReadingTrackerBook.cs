using System;

namespace Diarion.Models;

public class ReadingTrackerBook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SlotNumber { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public DateTime CompletedOn { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}