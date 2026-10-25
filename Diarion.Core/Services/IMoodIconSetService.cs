using Diarion.Models;

namespace Diarion.Services;

/// <summary>
/// Where the chosen <see cref="MoodIconSet"/> is kept. A device preference, like the theme and the
/// language: it is how this phone shows the diary, not part of the diary, so it is neither in the
/// database nor in a backup.
/// </summary>
public interface IMoodIconSetService
{
    /// <summary>The set in use; <see cref="MoodIconSet.Emoji"/> until the user picks another.</summary>
    MoodIconSet Current { get; }

    void Set(MoodIconSet set);
}
