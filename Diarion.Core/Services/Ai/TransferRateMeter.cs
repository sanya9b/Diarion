using System.Collections.Generic;

namespace Diarion.Services.Ai;

/// <summary>
/// Bytes per second, measured over a sliding window.
/// </summary>
/// <remarks>
/// Over a window rather than since the start, because an average taken from the start goes on
/// reporting the tunnel the phone drove through ten minutes ago. The window is the figure the user
/// can act on: it moves when the connection moves.
///
/// Time is passed in rather than read, so the arithmetic can be tested without waiting for it.
/// </remarks>
public sealed class TransferRateMeter
{
    /// <summary>Long enough that one megabyte on a slow line still lands inside it, short enough to react.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    /// <summary>Below this the divisor is noise and the quotient is nonsense.</summary>
    private static readonly TimeSpan MinimumSpan = TimeSpan.FromMilliseconds(250);

    private readonly Queue<(TimeSpan At, long Bytes)> _samples = new();

    private double _rate;

    /// <summary>
    /// Records a reading and answers with the rate as it now stands, or zero while there is
    /// nothing to divide by yet.
    /// </summary>
    public double Observe(TimeSpan at, long bytes)
    {
        _samples.Enqueue((at, bytes));

        // One expired sample is kept on purpose: on a connection slower than the window every
        // sample is expired, and dropping them all would leave nothing to measure against.
        while (_samples.Count > 2 && at - _samples.Peek().At > Window)
        {
            _samples.Dequeue();
        }

        var oldest = _samples.Peek();
        var span = at - oldest.At;
        if (span < MinimumSpan)
        {
            // Two reports in the same instant — most often a resumed file replaying what is
            // already on disk. Keep the previous answer rather than dividing by nothing.
            return _rate;
        }

        var delta = bytes - oldest.Bytes;
        _rate = delta <= 0 ? 0d : delta / span.TotalSeconds;
        return _rate;
    }

    /// <summary>
    /// Forgets the window. Used when the transfer stops moving bytes for a reason of its own, so
    /// the pause is not later reported as a collapse in speed.
    /// </summary>
    public void Reset()
    {
        _samples.Clear();
        _rate = 0d;
    }
}
