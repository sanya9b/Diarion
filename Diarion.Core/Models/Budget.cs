using System;

namespace Diarion.Models;

/// <summary>A monthly spending limit for an expense category.</summary>
public class Budget
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The expense category this budget caps (matched case-insensitively).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The spending limit per calendar month.</summary>
    public decimal MonthlyLimit { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
