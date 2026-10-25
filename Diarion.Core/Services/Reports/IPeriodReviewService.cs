using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Reports;

namespace Diarion.Services.Reports;

/// <summary>
/// The local review of a period: what changed against the same length of time just before it.
/// Nothing leaves the device and no model is involved; this is arithmetic over two snapshots.
/// </summary>
public interface IPeriodReviewService
{
    Task<PeriodReview> BuildAsync(StatsRange range, CancellationToken cancellationToken = default);
}
