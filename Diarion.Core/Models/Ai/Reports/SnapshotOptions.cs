namespace Diarion.Models.Ai.Reports;

/// <summary>
/// The parts of the diary a user has to switch on by hand before they leave the device.
/// </summary>
/// <remarks>
/// Only two, and both off: the general consent covers the diary as a diary, but sex and menstruation
/// are the two categories a person can be harmed by in ways the rest of the text cannot harm them,
/// and "I agreed to weekly reports" is not agreement to those. Everything else in the entry travels
/// under the one consent, which the preview screen shows in full.
/// </remarks>
public sealed record SnapshotOptions
{
    /// <summary>Nothing extra — what the first consent alone permits.</summary>
    public static readonly SnapshotOptions Default = new();

    public bool IncludeIntimateLife { get; init; }

    public bool IncludeCycle { get; init; }
}
