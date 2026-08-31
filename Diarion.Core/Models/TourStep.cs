namespace Diarion.Models;

/// <summary>Which screen a guide belongs to. One seen-marker per value, so the three never collide.</summary>
public enum TourId
{
    Home,
    Statistics,
    Settings
}

/// <summary>Which element a tour step points at.</summary>
/// <remarks>
/// An id rather than a coordinate: the view layer owns the geometry, and this enum is the whole of
/// what <see cref="Diarion.ViewModels.TourViewModel"/> is allowed to know about the screen it
/// explains. Adding a value here without teaching the page to resolve it is a step that highlights
/// nothing, so the two are kept in step by each page's own switch.
/// </remarks>
public enum TourStepId
{
    // Home
    QuickMenu,
    DayStrip,
    Tabs,
    DiaryBlocks,

    // Statistics
    StatsPeriod,
    StatsTabs,
    StatsThemes,
    StatsInfluences,

    // Settings
    SettingsTabs,
    SettingsBlocks,
    SettingsPrompts,
    SettingsSave
}

/// <summary>One coach mark: what is being pointed at, and what to say about it.</summary>
public class TourStep
{
    public TourStepId Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;
}
