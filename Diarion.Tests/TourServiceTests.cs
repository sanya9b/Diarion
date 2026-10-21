using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class TourServiceTests
{
    private readonly TourService _service = new();

    [Fact]
    public void GetSteps_ReadsTopToBottom_LikeTheScreenItExplains()
    {
        var steps = _service.GetSteps(TourId.Home, new UserProfile());

        steps.Select(s => s.Id).Should().Equal(
            TourStepId.QuickMenu,
            TourStepId.DayStrip,
            TourStepId.Tabs,
            TourStepId.DiaryBlocks);
    }

    [Fact]
    public void GetSteps_Statistics_ReadsTopToBottom()
    {
        var steps = _service.GetSteps(TourId.Statistics, new UserProfile());

        steps.Select(s => s.Id).Should().Equal(
            TourStepId.StatsPeriod,
            TourStepId.StatsTabs,
            TourStepId.StatsInfluences);
    }

    [Fact]
    public void GetSteps_Settings_ReadsTopToBottom()
    {
        var steps = _service.GetSteps(TourId.Settings, new UserProfile());

        steps.Select(s => s.Id).Should().Equal(
            TourStepId.SettingsTabs,
            TourStepId.SettingsBlocks,
            TourStepId.SettingsPrompts,
            TourStepId.SettingsSave);
    }

    [Theory]
    [InlineData(TourId.Home)]
    [InlineData(TourId.Statistics)]
    [InlineData(TourId.Settings)]
    public void GetSteps_AlwaysSaysSomething(TourId tour)
    {
        var steps = _service.GetSteps(tour, new UserProfile());

        steps.Should().NotBeEmpty();
        steps.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.Body));
    }

    [Fact]
    public void GetSteps_KeepsTheBlocksStep_WhileOneBlockSurvives()
    {
        var profile = new UserProfile
        {
            IsMoodBlockVisible = false,
            IsSleepBlockVisible = false,
            IsHealthBlockVisible = false,
            IsFoodBlockVisible = false,
            IsHabitsBlockVisible = false,
            IsGuidedPromptBlockVisible = false,
            IsReflectionBlockVisible = true
        };

        _service.GetSteps(TourId.Home, profile).Should().Contain(s => s.Id == TourStepId.DiaryBlocks);
    }

    [Fact]
    public void GetSteps_NeverReturnsNothing_EvenForTheEmptiestProfile()
    {
        var profile = new UserProfile
        {
            IsMoodBlockVisible = false,
            IsSleepBlockVisible = false,
            IsHealthBlockVisible = false,
            IsFoodBlockVisible = false,
            IsHabitsBlockVisible = false,
            IsGuidedPromptBlockVisible = false,
            IsReflectionBlockVisible = false
        };

        // The strip, the date and the tabs are on the screen whatever the profile says, so the guide
        // is never reduced to an empty scrim.
        _service.GetSteps(TourId.Home, profile).Should().HaveCount(3);
    }

    [Theory]
    [InlineData(TourId.Home)]
    [InlineData(TourId.Statistics)]
    [InlineData(TourId.Settings)]
    public void ShouldOffer_IsTrue_ForAProfileWrittenBeforeTheGuidesExisted(TourId tour)
    {
        // LiteDB returns 0 for a field the stored document does not have, which is the whole migration.
        _service.ShouldOffer(tour, new UserProfile()).Should().BeTrue();
    }

    [Fact]
    public void MarkSeen_MarksOnlyTheGuideItWasGiven()
    {
        var profile = new UserProfile();

        _service.MarkSeen(TourId.Statistics, profile);

        _service.ShouldOffer(TourId.Statistics, profile).Should().BeFalse();
        _service.ShouldOffer(TourId.Home, profile).Should().BeTrue();
        _service.ShouldOffer(TourId.Settings, profile).Should().BeTrue();
    }

    [Fact]
    public void ResetSeen_ForgetsEveryGuide()
    {
        var profile = new UserProfile();

        _service.MarkSeen(TourId.Home, profile);
        _service.MarkSeen(TourId.Statistics, profile);
        _service.MarkSeen(TourId.Settings, profile);

        _service.ResetSeen(profile);

        _service.ShouldOffer(TourId.Home, profile).Should().BeTrue();
        _service.ShouldOffer(TourId.Statistics, profile).Should().BeTrue();
        _service.ShouldOffer(TourId.Settings, profile).Should().BeTrue();
    }
}
