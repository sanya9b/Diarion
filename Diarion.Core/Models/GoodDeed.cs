using System;

namespace Diarion.Models;

public class GoodDeed
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SlotNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}