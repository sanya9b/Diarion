using System;

namespace Diarion.Models;

/// <summary>
/// A single day the user marked as a period day. One row per day rather than an episode with a start
/// and an end: marking is then an idempotent insert or delete of one row, and splitting or merging an
/// episode falls out of grouping consecutive dates instead of needing its own editing UI.
/// </summary>
public class CycleLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
