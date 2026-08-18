using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Diarion.Models;

namespace Diarion.ViewModels;

/// <summary>Which list on the onboarding picker a module belongs to.</summary>
public enum OnboardingModuleGroup
{
    /// <summary>A section of the daily entry — one of the home-screen block flags.</summary>
    DailyEntry,

    /// <summary>A screen of its own, reached from the quick menu.</summary>
    Section
}

/// <summary>
/// One line of the onboarding picker: a name, where it belongs, and the two places a yes or no lands.
/// </summary>
/// <remarks>
/// The mapping to <see cref="UserProfile"/> is a delegate rather than a switch in the service so the
/// catalogue and the writer cannot drift apart — a module that is listed and never applied would look
/// exactly like one that works. Sits next to <see cref="QuickMenuItem"/> for the same reason it does:
/// it is bound to directly by a template and has no life outside the screen that shows it.
/// </remarks>
public partial class OnboardingModule : ObservableObject
{
    public required string Id { get; init; }

    /// <summary>Already localised — the catalogue is built per call, after the culture is set.</summary>
    public required string Title { get; init; }

    public required OnboardingModuleGroup Group { get; init; }

    /// <summary>Writes the profile flag this module owns. Null when the module is only a tile.</summary>
    public Action<UserProfile, bool>? SetEnabled { get; init; }

    /// <summary>The quick-menu tile this module brings forward. Null when it has none.</summary>
    public string? QuickMenuId { get; init; }

    /// <summary>
    /// Checked to begin with, matching every one of the profile defaults it writes. The picker is
    /// therefore a place to take things away, which is the only direction that is safe to get wrong:
    /// nobody loses a feature by not noticing this screen.
    /// </summary>
    [ObservableProperty] private bool _isSelected = true;
}
