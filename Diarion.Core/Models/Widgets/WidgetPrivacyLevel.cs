namespace Diarion.Models.Widgets;

/// <summary>
/// How much a home-screen widget may show without the app being opened (spec 20, D2). The widget
/// never reads the database: the app publishes a snapshot, and this level decides what goes into it.
/// </summary>
public enum WidgetPrivacyLevel
{
    /// <summary>A logo and an "open" button. The snapshot carries the date and nothing else.</summary>
    ButtonOnly = 0,

    /// <summary>Streak, the mood glyph of the day, "2 of 5 tasks", "3 of 4 habits". No text of the user's.</summary>
    Counts = 1,

    /// <summary>Also the names of tasks and habits. Chosen by hand under a warning, never by default.</summary>
    WithText = 2
}

/// <summary>The one rule for the level in force: what the user chose, or a default that follows the app lock.</summary>
public static class WidgetPrivacyPolicy
{
    /// <summary>
    /// A chosen level wins. Without a choice, a locked app shows only the button — the lock exists
    /// against someone glancing at an unlocked phone (spec 04), and a widget would hand them the
    /// day's numbers past it — and an unlocked app shows the counts.
    /// </summary>
    public static WidgetPrivacyLevel Resolve(WidgetPrivacyLevel? chosen, bool lockEnabled)
        => chosen ?? (lockEnabled ? WidgetPrivacyLevel.ButtonOnly : WidgetPrivacyLevel.Counts);
}
