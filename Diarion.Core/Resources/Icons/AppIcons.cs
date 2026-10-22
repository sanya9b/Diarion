using Microsoft.Maui.Controls.Shapes;

namespace Diarion.Resources.Icons;

/// <summary>
/// Line icons drawn on a 24-unit grid, for the places that used colour emoji: statistics tabs and their
/// empty states, the streak, the period chips, the hour chip, the theme switch and the PIN screens.
/// An emoji is drawn by the platform's colour font, so it looks different on iOS, Android and Windows
/// and ignores the theme; these take the stroke colour of the <see cref="Path"/> they sit in, which keeps
/// the panels monochrome as the design rules ask. Draw them with a round line cap and join, stroke 1.8–2,
/// and <c>Aspect="Uniform"</c>.
/// Each property hands out a fresh geometry, so no instance is ever shared between two paths.
/// The mood faces keep their emoji on purpose: the face row was the owner's decision in spec 18.
/// </summary>
public static class AppIcons
{
    public const string SmilePath =
        "M 2 12 A 10 10 0 1 1 22 12 A 10 10 0 1 1 2 12 Z M 8 14.5 Q 12 18 16 14.5 M 9 9.5 L 9 10 M 15 9.5 L 15 10";

    public const string MoonPath = "M 20.5 13.5 A 8.5 8.5 0 1 1 10.5 3.5 A 6.5 6.5 0 0 0 20.5 13.5 Z";

    public const string CheckCirclePath = "M 2 12 A 10 10 0 1 1 22 12 A 10 10 0 1 1 2 12 Z M 8 12.5 L 11 15.5 L 16.5 9.5";

    public const string BanknotePath = "M 2.5 6.5 L 21.5 6.5 L 21.5 17.5 L 2.5 17.5 Z M 9.5 12 A 2.5 2.5 0 1 1 14.5 12 A 2.5 2.5 0 1 1 9.5 12 Z M 5.5 9.5 L 5.5 9.5 M 18.5 14.5 L 18.5 14.5";

    public const string SproutPath =
        "M 12 21 L 12 11 M 12 11 C 12 7.5 9.5 5 5 5 C 5 9 7.5 11 12 11 M 12 13.5 C 12 10 14.5 7.5 19 7.5 C 19 11.5 16.5 13.5 12 13.5";

    public const string DropPath = "M 12 3 C 12 3 5.5 10 5.5 14.5 A 6.5 6.5 0 0 0 18.5 14.5 C 18.5 10 12 3 12 3 Z";

    public const string CalendarPath = "M 4 6 L 20 6 L 20 20 L 4 20 Z M 4 10 L 20 10 M 8.5 3.5 L 8.5 7.5 M 15.5 3.5 L 15.5 7.5";

    public const string FlamePath =
        "M 12 3 C 13 6.5 17.5 9 17.5 14 A 5.5 5.5 0 0 1 6.5 14 C 6.5 11.5 8 9.8 9.3 8.8 C 9.3 10.6 10.2 11.8 11.4 12.2 C 11 9 11.4 5.8 12 3 Z";

    public const string ClockPath = "M 3 12 A 9 9 0 1 1 21 12 A 9 9 0 1 1 3 12 Z M 12 7.5 L 12 12 L 15 14";

    public const string PencilPath = "M 4 20 L 8.5 19 L 19 8.5 L 15.5 5 L 5 15.5 Z M 13.5 7 L 17 10.5";

    public const string SunPath =
        "M 8 12 A 4 4 0 1 1 16 12 A 4 4 0 1 1 8 12 Z M 12 2.5 L 12 4.5 M 12 19.5 L 12 21.5 M 2.5 12 L 4.5 12 M 19.5 12 L 21.5 12 " +
        "M 5.3 5.3 L 6.7 6.7 M 17.3 17.3 L 18.7 18.7 M 5.3 18.7 L 6.7 17.3 M 17.3 6.7 L 18.7 5.3";

    public const string BlossomPath =
        "M 9.5 7 A 2.5 2.5 0 1 1 14.5 7 A 2.5 2.5 0 1 1 9.5 7 Z M 14.5 12 A 2.5 2.5 0 1 1 19.5 12 A 2.5 2.5 0 1 1 14.5 12 Z " +
        "M 9.5 17 A 2.5 2.5 0 1 1 14.5 17 A 2.5 2.5 0 1 1 9.5 17 Z M 4.5 12 A 2.5 2.5 0 1 1 9.5 12 A 2.5 2.5 0 1 1 4.5 12 Z " +
        "M 10.8 12 A 1.2 1.2 0 1 1 13.2 12 A 1.2 1.2 0 1 1 10.8 12 Z";

    public const string LockPath = "M 5.5 11 L 18.5 11 L 18.5 21 L 5.5 21 Z M 8 11 L 8 7.5 A 4 4 0 0 1 16 7.5 L 16 11 M 12 15 L 12 17";

    public const string FingerprintPath =
        "M 6.5 18 C 5.5 16 5 14 5 12 A 7 7 0 0 1 19 12 C 19 13.5 18.8 15 18.4 16.4 M 9 20 C 8.3 18 8 15.5 8 12 A 4 4 0 0 1 16 12 " +
        "C 16 14.5 15.6 17 14.8 19.2 M 12 21 C 11.4 18.5 11 15.5 11 12 A 1 1 0 0 1 13 12 C 13 14 12.8 16 12.4 18";

    /// <summary>Three bars on a baseline: the "Review" tab, this period set against the one before.</summary>
    public const string BarsPath = "M 3.5 20.5 L 20.5 20.5 M 7 17 L 7 12 M 12 17 L 12 6.5 M 17 17 L 17 9.5";

    public static Geometry Bars => Parse(BarsPath);

    /// <summary>An autumn leaf with its midrib — the streak tile from September to November.</summary>
    public const string LeafPath = "M 5 19 C 5 10.5 10.5 5 19 5 C 19 13.5 13.5 19 5 19 Z M 5 19 L 14 10";

    /// <summary>
    /// A six-armed snowflake — the streak tile from December to February. The arms and their chevrons are
    /// one shape rotated by 60° about the centre, so all six are identical rather than drawn by eye.
    /// </summary>
    public const string SnowflakePath =
        "M 12 3 L 12 21 M 19.8 7.5 L 4.2 16.5 M 19.8 16.5 L 4.2 7.5 " +
        "M 9.6 4.4 L 12 6.8 L 14.4 4.4 M 17.4 6.1 L 16.5 9.4 L 19.8 10.3 M 19.8 13.7 L 16.5 14.6 L 17.4 17.9 " +
        "M 14.4 19.6 L 12 17.2 L 9.6 19.6 M 6.6 17.9 L 7.5 14.6 L 4.2 13.7 M 4.2 10.3 L 7.5 9.4 L 6.6 6.1";

    public static Geometry Leaf => Parse(LeafPath);
    public static Geometry Snowflake => Parse(SnowflakePath);
    public static Geometry Smile => Parse(SmilePath);
    public static Geometry Moon => Parse(MoonPath);
    public static Geometry CheckCircle => Parse(CheckCirclePath);
    public static Geometry Banknote => Parse(BanknotePath);
    public static Geometry Sprout => Parse(SproutPath);
    public static Geometry Drop => Parse(DropPath);
    public static Geometry Calendar => Parse(CalendarPath);
    public static Geometry Flame => Parse(FlamePath);
    public static Geometry Clock => Parse(ClockPath);
    public static Geometry Pencil => Parse(PencilPath);
    public static Geometry Sun => Parse(SunPath);
    public static Geometry Blossom => Parse(BlossomPath);
    public static Geometry Lock => Parse(LockPath);
    public static Geometry Fingerprint => Parse(FingerprintPath);

    public static Geometry Parse(string pathData) =>
        (Geometry)new PathGeometryConverter().ConvertFromInvariantString(pathData)!;
}
