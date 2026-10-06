using System;

namespace Diarion.Models;

/// <summary>
/// A money account / wallet (cash, card, savings, …). Transactions belong to an account via
/// <see cref="FinanceTransaction.AccountId"/>; an account's balance is its <see cref="InitialBalance"/>
/// plus the net of its transactions. No transfers between accounts (out of scope for Phase B).
/// </summary>
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Set on the migration-created default account so its name follows the UI language. Cleared the
    /// moment the user edits the account, after which <see cref="Name"/> is authoritative.
    /// </summary>
    public string? ResourceKey { get; set; }

    /// <summary>Emoji glyph shown in the account chip.</summary>
    public string Icon { get; set; } = "💳";

    /// <summary>Accent color (hex) for the account chip.</summary>
    public string ColorHex { get; set; } = "#8FA083";

    /// <summary>Opening balance the account started with, before any recorded transactions.</summary>
    public decimal InitialBalance { get; set; }

    /// <summary>Reserved: hides the account without deleting its history (no UI yet).</summary>
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
