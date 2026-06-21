using System;

namespace Diarion.Models;

public class WishlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WantText { get; set; } = string.Empty;
    public string WishText { get; set; } = string.Empty;
    public string GetText { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsCompleted { get; set; }
}
