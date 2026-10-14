using System;
using System.Collections.Generic;
using Diarion.Models;
using Diarion.Resources.Localization;

namespace Diarion.Services;

/// <inheritdoc />
public class TourService : ITourService
{
    /// <summary>
    /// Bump when a guide gains a step worth re-showing to people who already sat through it.
    /// </summary>
    /// <remarks>
    /// Version 1 is also what every profile written before the guides existed reads as: the fields are
    /// absent from those documents, LiteDB returns 0, and 0 &lt; 1 means they are offered each guide
    /// exactly once. That is the whole migration.
    /// <para>
    /// One number for all three on purpose. Separate versions would only earn their keep if one
    /// screen's guide could change without the others, and the day that happens is the day to split
    /// it — not before.
    /// </para>
    /// </remarks>
    public const int CurrentVersion = 1;

    /// <inheritdoc />
    /// <remarks>
    /// Built per call rather than cached, for the same reason as
    /// <see cref="OnboardingModuleService.GetModules"/>: the text comes out of
    /// <see cref="AppResources"/>, and a static list would freeze whichever language was current when
    /// the type was first touched.
    /// <para>
    /// The order is the order of the screen, top to bottom, because a guide that jumps around is
    /// harder to follow than the screen it is explaining.
    /// </para>
    /// </remarks>
    public IReadOnlyList<TourStep> GetSteps(TourId tour, UserProfile profile) => tour switch
    {
        TourId.Home => HomeSteps(profile),
        TourId.Statistics => StatisticsSteps(),
        TourId.Settings => SettingsSteps(),
        _ => Array.Empty<TourStep>()
    };

    /// <inheritdoc />
    public bool ShouldOffer(TourId tour, UserProfile profile) => SeenVersion(tour, profile) < CurrentVersion;

    /// <inheritdoc />
    public void MarkSeen(TourId tour, UserProfile profile)
    {
        switch (tour)
        {
            case TourId.Home:
                profile.TourVersionSeen = CurrentVersion;
                break;
            case TourId.Statistics:
                profile.StatsTourVersionSeen = CurrentVersion;
                break;
            case TourId.Settings:
                profile.SettingsTourVersionSeen = CurrentVersion;
                break;
        }
    }

    /// <inheritdoc />
    public void ResetSeen(UserProfile profile)
    {
        profile.TourVersionSeen = 0;
        profile.StatsTourVersionSeen = 0;
        profile.SettingsTourVersionSeen = 0;

        // Not a guide, but dismissed like one, and this is the one place that promises everything
        // from the start again (spec 17, D5).
        profile.InsightPathHiddenBelowMilestone = 0;
    }

    private static int SeenVersion(TourId tour, UserProfile profile) => tour switch
    {
        TourId.Home => profile.TourVersionSeen,
        TourId.Statistics => profile.StatsTourVersionSeen,
        TourId.Settings => profile.SettingsTourVersionSeen,

        // An id nobody has taught this service about is not a guide anyone should be offered.
        _ => CurrentVersion
    };

    private static List<TourStep> HomeSteps(UserProfile profile)
    {
        var steps = new List<TourStep>
        {
            new()
            {
                Id = TourStepId.QuickMenu,
                Title = AppResources.TourQuickMenuTitle,
                Body = AppResources.TourQuickMenuText
            },
            new()
            {
                Id = TourStepId.DayStrip,
                Title = AppResources.TourDayStripTitle,
                Body = AppResources.TourDayStripText
            },
            new()
            {
                Id = TourStepId.Tabs,
                Title = AppResources.TourTabsTitle,
                Body = AppResources.TourTabsText
            }
        };

        // Somebody who unchecked every block on the onboarding module screen has an empty diary tab,
        // and a spotlight around nothing reads as a bug. The other three targets are structural — the
        // strip, the date and the tabs are on the screen whatever the profile says.
        if (HasAnyDiaryBlock(profile))
        {
            steps.Add(new TourStep
            {
                Id = TourStepId.DiaryBlocks,
                Title = AppResources.TourBlocksTitle,
                Body = AppResources.TourBlocksText
            });
        }

        return steps;
    }

    /// <remarks>
    /// Nothing here is conditional on the profile, unlike the home screen. The themes and influence
    /// cards do come and go — but with how much has been written, not with a setting, and the guide
    /// runs on the first visit, when the answer is always "nothing yet". So both steps say what will
    /// appear rather than what is there, and the page draws no hole when the card is absent.
    /// </remarks>
    private static List<TourStep> StatisticsSteps() => new()
    {
        new()
        {
            Id = TourStepId.StatsPeriod,
            Title = AppResources.TourStatsPeriodTitle,
            Body = AppResources.TourStatsPeriodText
        },
        new()
        {
            Id = TourStepId.StatsTabs,
            Title = AppResources.TourStatsTabsTitle,
            Body = AppResources.TourStatsTabsText
        },
        new()
        {
            Id = TourStepId.StatsThemes,
            Title = AppResources.TourStatsThemesTitle,
            Body = AppResources.TourStatsThemesText
        },
        new()
        {
            Id = TourStepId.StatsInfluences,
            Title = AppResources.TourStatsInfluencesTitle,
            Body = AppResources.TourStatsInfluencesText
        }
    };

    private static List<TourStep> SettingsSteps() => new()
    {
        new()
        {
            Id = TourStepId.SettingsTabs,
            Title = AppResources.TourSettingsTabsTitle,
            Body = AppResources.TourSettingsTabsText
        },
        new()
        {
            Id = TourStepId.SettingsBlocks,
            Title = AppResources.TourSettingsBlocksTitle,
            Body = AppResources.TourSettingsBlocksText
        },
        new()
        {
            Id = TourStepId.SettingsPrompts,
            Title = AppResources.TourSettingsPromptsTitle,
            Body = AppResources.TourSettingsPromptsText
        },
        new()
        {
            Id = TourStepId.SettingsSave,
            Title = AppResources.TourSettingsSaveTitle,
            Body = AppResources.TourSettingsSaveText
        }
    };

    private static bool HasAnyDiaryBlock(UserProfile profile)
        => profile.IsMoodBlockVisible
           || profile.IsSleepBlockVisible
           || profile.IsHealthBlockVisible
           || profile.IsFoodBlockVisible
           || profile.IsHabitsBlockVisible
           || profile.IsGuidedPromptBlockVisible
           || profile.IsReflectionBlockVisible;
}
