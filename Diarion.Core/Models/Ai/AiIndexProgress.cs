namespace Diarion.Models.Ai;

public enum AiIndexPhase
{
    /// <summary>Nothing scheduled — either AI is off or the index is already complete.</summary>
    Idle,

    /// <summary>Comparing sources against stored rows to work out what still needs embedding.</summary>
    Scanning,

    /// <summary>Embedding batches.</summary>
    Embedding,

    /// <summary>Stopped before finishing — app going to sleep, or the user turned AI off.</summary>
    Cancelled,

    /// <summary>Every source has a current row for the active model.</summary>
    Complete,
}

/// <summary>
/// Snapshot of indexing progress. A struct raised through an event rather than
/// <see cref="System.IProgress{T}"/>: the app has no background-work infrastructure, and a single
/// coordinator with one event is the smallest thing that reports honestly.
/// </summary>
public readonly record struct AiIndexProgress(AiIndexPhase Phase, int Done, int Total)
{
    public static AiIndexProgress Idle => new(AiIndexPhase.Idle, 0, 0);

    /// <summary>Fraction complete in 0..1. Zero total reads as complete, not as division by zero.</summary>
    public double Fraction => Total <= 0 ? 1d : Math.Clamp((double)Done / Total, 0d, 1d);
}
