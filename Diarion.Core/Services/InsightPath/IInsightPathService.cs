using System.Threading.Tasks;

namespace Diarion.Services.InsightPath;

/// <summary>
/// The path from an empty diary to the first verified hypothesis, as the home screen shows it
/// (spec 17). Reads the correlation engine and never changes what it computes (R0.1).
/// </summary>
public interface IInsightPathService
{
    /// <summary>
    /// Where the user stands today, over the last <see cref="InsightPathService.WindowDays"/> days.
    /// Meant to be called after every save: the readiness pass leaves text themes out, so it never
    /// waits on the encoder, and the hypothesis check is the same pass the statistics screen runs.
    /// </summary>
    Task<InsightPathState> GetAsync();

    /// <summary>
    /// "Hide" on the card: keeps it off the screen until the next milestone is reached, or for good
    /// after the last one (spec 17, D5). Persisted through the profile; "show the guides again"
    /// undoes it.
    /// </summary>
    Task HideUntilNextMilestoneAsync();
}
