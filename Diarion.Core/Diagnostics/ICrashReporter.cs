using System;

namespace Diarion.Diagnostics;

/// <summary>
/// Keeps the last crash so the next launch can show it. There is exactly one slot: a crash that
/// repeats writes over itself, and the most recent one is the one worth reading.
/// </summary>
public interface ICrashReporter
{
    /// <summary>Best-effort write. Called from an unhandled-exception handler, so it must never throw.</summary>
    void Record(string source, Exception? exception);

    /// <summary>The stored report, or null if there is none.</summary>
    string? ReadLast();

    bool HasReport { get; }

    /// <summary>
    /// Where the report sits, or null when there is none. Exposed so it can go straight to the share
    /// sheet: the report is already a file, and copying it somewhere else first would only put a
    /// second copy of a crash on disk.
    /// </summary>
    string? ReportPath { get; }

    void Clear();
}
