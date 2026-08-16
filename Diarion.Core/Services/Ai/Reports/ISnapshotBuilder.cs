using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai.Reports;

namespace Diarion.Services.Ai.Reports;

public interface ISnapshotBuilder
{
    /// <summary>
    /// Gathers everything the app knows about <paramref name="range"/> into the one object that is both
    /// shown to the user and sent to the provider.
    /// </summary>
    /// <remarks>
    /// <paramref name="kind"/> is passed rather than inferred from the length of the window: thirty-one
    /// days is a month, and is also a range someone dragged out on the statistics screen, and the
    /// snapshot has to know which of the two it is.
    /// </remarks>
    Task<PeriodSnapshot> BuildAsync(
        PeriodKind kind,
        StatsRange range,
        SnapshotOptions options,
        CancellationToken cancellationToken = default);
}
