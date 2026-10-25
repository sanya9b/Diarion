using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Models.Reports;
using Diarion.Services.Ai.Reports;

namespace Diarion.Services.Reports;

/// <summary>
/// Builds the two snapshots and hands them to the calculator. The snapshot builder already reads
/// every service the review needs, so this class owns no data access of its own.
/// </summary>
public sealed class PeriodReviewService : IPeriodReviewService
{
    private readonly ISnapshotBuilder _snapshots;

    public PeriodReviewService(ISnapshotBuilder snapshots)
    {
        _snapshots = snapshots;
    }

    public async Task<PeriodReview> BuildAsync(StatsRange range, CancellationToken cancellationToken = default)
    {
        range = range.Normalized();
        var previousRange = PreviousOf(range);
        var kind = KindFor(range);

        // Default options: intimate life and the cycle stay out, as they do for anything that is not
        // the user's own explicit choice. Neither feeds a number the review shows, so nothing is lost.
        var current = await _snapshots.BuildAsync(kind, range, SnapshotOptions.Default, cancellationToken);
        var previous = await _snapshots.BuildAsync(kind, previousRange, SnapshotOptions.Default, cancellationToken);

        return PeriodReviewCalculator.Compare(current, previous, range, previousRange);
    }

    /// <summary>The window of the same length ending the day before <paramref name="range"/> starts.</summary>
    public static StatsRange PreviousOf(StatsRange range)
    {
        range = range.Normalized();
        return new StatsRange(range.Start.AddDays(-range.Days), range.Start.AddDays(-1));
    }

    /// <summary>
    /// The snapshot wants to be told which cadence it is. The review compares arbitrary windows, so
    /// the nearest label by length is the honest answer — it is a label, not a boundary.
    /// </summary>
    public static PeriodKind KindFor(StatsRange range) => range.Normalized().Days switch
    {
        <= 7 => PeriodKind.Week,
        <= 31 => PeriodKind.Month,
        <= 92 => PeriodKind.Quarter,
        _ => PeriodKind.Year
    };
}
