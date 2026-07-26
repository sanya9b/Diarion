namespace Diarion.Models;

/// <summary>
/// One mood observation pinned to an hour of the day. Stored as a list rather than an hour-keyed
/// dictionary so it binds like every other repeated element in the app (see
/// <see cref="DiaryEntry.HabitsList"/>) and stays readable in backups and exports.
/// </summary>
public class HourMood
{
    public int Hour { get; set; }

    public Emotion Mood { get; set; }
}
