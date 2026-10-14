using System.Collections.Generic;
using Diarion.Models;

namespace Diarion.Services;

/// <summary>The running order of the coach marks on each guided screen, and the record of having seen them.</summary>
public interface ITourService
{
    /// <summary>
    /// The steps worth showing this profile on <paramref name="tour"/>, in the order the screen reads.
    /// A step whose target the profile has switched off is left out rather than shown over an empty
    /// space.
    /// </summary>
    IReadOnlyList<TourStep> GetSteps(TourId tour, UserProfile profile);

    /// <summary>Whether this profile has yet to see the current version of <paramref name="tour"/>.</summary>
    bool ShouldOffer(TourId tour, UserProfile profile);

    /// <summary>Records one guide as seen. The caller saves the profile.</summary>
    void MarkSeen(TourId tour, UserProfile profile);

    /// <summary>
    /// Forgets every guide, so each screen offers its own again on the next visit. Deliberately not
    /// per-tour: the settings entry that calls this reads as "show me the guides again", and somebody
    /// who asks that has not gone looking for a per-screen switch. The dismissed first-insight
    /// path on the home screen comes back with them, for the same reason.
    /// </summary>
    void ResetSeen(UserProfile profile);
}
