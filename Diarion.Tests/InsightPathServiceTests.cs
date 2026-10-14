using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.Services.Database;
using Diarion.Services.InsightPath;
using Diarion.Services.Store;
using FluentAssertions;
using Moq;
using Xunit;
using Factors = Diarion.Services.CorrelationService.Factors;

namespace Diarion.Tests;

/// <summary>
/// The home-screen path to a first hypothesis (spec 17). The first half pins the visibility rule
/// with the engine replaced by its two answers; the second runs the real engine, built the way
/// <see cref="CorrelationServiceTests"/> builds it, over actual days.
/// </summary>
public class InsightPathServiceTests
{
    /// <summary>The person the card is for: a fortnight into the trial, writing.</summary>
    private static readonly AccessState Trial =
        new(true, AccessTier.Base, AccessSource.Trial, DateTime.UtcNow.AddDays(14), true);

    private const int Required = CorrelationService.MinSampleSize;

    private static MoodCorrelation Sleep(int confidence = 4, double r = 0.6, int n = Required) => new()
    {
        FactorKey = Factors.SleepDuration,
        Coefficient = r,
        Confidence = confidence,
        SampleSize = n
    };

    // ---- the rule, with the engine mocked --------------------------------------------------

    private sealed class Harness
    {
        public Mock<ICorrelationService> Engine { get; } = new();
        public Mock<IProfileService> Profiles { get; } = new();
        public UserProfile Profile { get; } = new();
        public InsightPathService Service { get; }

        public Harness(
            CorrelationReadiness readiness,
            IReadOnlyList<MoodCorrelation>? holding = null,
            AccessState? access = null)
        {
            Readiness(readiness);
            Holding(holding ?? new List<MoodCorrelation>());
            Profiles.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(Profile);
            Profiles.Setup(p => p.SaveUserProfileAsync(It.IsAny<UserProfile>())).Returns(Task.CompletedTask);

            Service = new InsightPathService(Engine.Object, Profiles.Object, new FixedEntitlementService(access ?? Trial));
        }

        public void Readiness(CorrelationReadiness readiness) =>
            Engine.Setup(e => e.GetReadinessAsync(It.IsAny<StatsRange>(), It.IsAny<int>(), It.IsAny<bool>()))
                  .ReturnsAsync(readiness);

        public void Holding(IReadOnlyList<MoodCorrelation> holding) =>
            Engine.Setup(e => e.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()))
                  .ReturnsAsync(holding);
    }

    [Fact]
    public async Task AC1_AnEmptyDiaryInWriteMode_ShowsTheStartOfThePath()
    {
        var h = new Harness(new CorrelationReadiness(0, Required));

        var state = await h.Service.GetAsync();

        state.IsVisible.Should().BeTrue();
        state.PairedDays.Should().Be(0);
        state.RequiredDays.Should().Be(Required);
        state.BestFactorKey.Should().BeNull("the hint has no factor to name yet");
        state.ReachedMilestone.Should().Be(0);
        state.NextMilestone.Should().Be(1);
        state.FirstHypothesis.Should().BeNull();
    }

    [Fact]
    public async Task AC2_NinePairedDays_StandAtTheSeventhMilestone_WithSleepAsTheHint()
    {
        var h = new Harness(new CorrelationReadiness(9, Required, Factors.SleepDuration));

        var state = await h.Service.GetAsync();

        state.IsVisible.Should().BeTrue();
        state.PairedDays.Should().Be(9);
        state.BestFactorKey.Should().Be(Factors.SleepDuration);
        state.ReachedMilestone.Should().Be(7);
        state.NextMilestone.Should().Be(14);
        state.FirstHypothesis.Should().BeNull("the hypothesis belongs to the last milestone alone");
    }

    [Fact]
    public async Task AC4_AtFourteenWithAHoldingHypothesis_TheCardShowsIt_UntilHidden_AndThenNeverAgain()
    {
        var sleep = Sleep();
        var h = new Harness(new CorrelationReadiness(14, Required, Factors.SleepDuration), [sleep]);

        var state = await h.Service.GetAsync();

        state.IsVisible.Should().BeTrue();
        state.ReachedMilestone.Should().Be(14);
        state.NextMilestone.Should().BeNull();
        state.FirstHypothesis.Should().BeSameAs(sleep);
        state.FirstHypothesis!.SampleSize.Should().Be(14);

        await h.Service.HideUntilNextMilestoneAsync();

        h.Profile.InsightPathHiddenBelowMilestone.Should().Be(int.MaxValue, "there is no milestone after the last");
        h.Profiles.Verify(p => p.SaveUserProfileAsync(h.Profile), Times.Once);
        (await h.Service.GetAsync()).IsVisible.Should().BeFalse();

        // A new entry the next day: one more paired day, and still nothing new to say.
        h.Readiness(new CorrelationReadiness(15, Required, Factors.SleepDuration));
        (await h.Service.GetAsync()).IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task FirstHypothesis_IsTheMostConfident_ThenTheLargest()
    {
        var sureButSmall = new MoodCorrelation { FactorKey = Factors.MealsLogged, Coefficient = 0.3, Confidence = 5 };
        var largeButUnsure = new MoodCorrelation { FactorKey = Factors.SleepDuration, Coefficient = -0.8, Confidence = 3 };
        var sureAndLarger = new MoodCorrelation { FactorKey = Factors.HabitCompletion, Coefficient = -0.5, Confidence = 5 };
        var tested = new MoodCorrelation { FactorKey = Factors.DailySpend, Coefficient = 0.9, Confidence = 2 };
        var h = new Harness(
            new CorrelationReadiness(14, Required, Factors.SleepDuration),
            [largeButUnsure, tested, sureButSmall, sureAndLarger]);

        (await h.Service.GetAsync()).FirstHypothesis.Should().BeSameAs(sureAndLarger);
    }

    [Fact]
    public async Task AC5_FourteenDaysWithNothingHolding_IsNotAMilestone()
    {
        // The engine reports every factor it could test; two dots is a test that failed, not a finding.
        var h = new Harness(new CorrelationReadiness(14, Required, Factors.SleepDuration), [Sleep(confidence: 2)]);

        var state = await h.Service.GetAsync();

        state.IsVisible.Should().BeFalse("the statistics screen already says that no hypothesis holds");
        state.ReachedMilestone.Should().Be(14);
        state.FirstHypothesis.Should().BeNull();
    }

    [Fact]
    public async Task AC6_ReadOnly_HidesTheCard_WhateverTheDataSays()
    {
        var h = new Harness(new CorrelationReadiness(3, Required, Factors.SleepDuration), access: AccessState.ReadOnly);

        (await h.Service.GetAsync()).IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task AC6_ALegacyUserWhoseHypothesisAlreadyHolds_NeverSeesIt()
    {
        var h = new Harness(new CorrelationReadiness(30, Required, Factors.SleepDuration), [Sleep(n: 30)], AccessState.Legacy);

        (await h.Service.GetAsync()).IsVisible.Should().BeFalse("a finding they have had for months is not a first");
    }

    [Fact]
    public async Task ALegacyUserWithoutAHypothesis_WalksThePathLikeAnyoneElse()
    {
        var h = new Harness(new CorrelationReadiness(5, Required, Factors.SleepQuality), access: AccessState.Legacy);

        (await h.Service.GetAsync()).IsVisible.Should().BeTrue("the hint can still tell them what is missing");
    }

    [Fact]
    public async Task AHypothesisThatHoldsBeforeTheLastMilestone_HidesTheCard()
    {
        // A text theme can clear the bar while no logged factor has: themes are kept out of the
        // count but not out of the hypothesis check. Someone with a finding is not on the path.
        var theme = new MoodCorrelation { FactorKey = Factors.ThemePrefix + "Work", Coefficient = -0.6, Confidence = 4 };
        var h = new Harness(new CorrelationReadiness(9, Required, Factors.SleepDuration), [theme]);

        var state = await h.Service.GetAsync();

        state.IsVisible.Should().BeFalse();
        state.FirstHypothesis.Should().BeNull();
    }

    [Fact]
    public async Task Hiding_WaitsForTheNextMilestone_NotForever()
    {
        var h = new Harness(new CorrelationReadiness(8, Required, Factors.SleepDuration));

        await h.Service.HideUntilNextMilestoneAsync();

        h.Profile.InsightPathHiddenBelowMilestone.Should().Be(14);
        (await h.Service.GetAsync()).IsVisible.Should().BeFalse();

        h.Readiness(new CorrelationReadiness(13, Required, Factors.SleepDuration));
        (await h.Service.GetAsync()).IsVisible.Should().BeFalse("thirteen is still the seventh milestone");

        // At fourteen the card comes back only with something to show (AC4), never empty-handed (AC5).
        h.Readiness(new CorrelationReadiness(14, Required, Factors.SleepDuration));
        (await h.Service.GetAsync()).IsVisible.Should().BeFalse();
        h.Holding([Sleep()]);
        (await h.Service.GetAsync()).IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Hiding_DoesNotRunTheHypothesisPass()
    {
        var h = new Harness(new CorrelationReadiness(2, Required, Factors.SleepDuration));

        await h.Service.HideUntilNextMilestoneAsync();

        h.Engine.Verify(e => e.GetMoodCorrelationsAsync(It.IsAny<StatsRange>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task TheWindow_IsTheLastThirtyDays_AndReadinessLeavesThemesOut()
    {
        var h = new Harness(new CorrelationReadiness(0, Required));

        await h.Service.GetAsync();

        h.Engine.Verify(
            e => e.GetReadinessAsync(It.Is<StatsRange>(r => r.Days == InsightPathService.WindowDays && r.End == DateTime.Today), 0, false),
            Times.Once);
        h.Engine.Verify(
            e => e.GetMoodCorrelationsAsync(It.Is<StatsRange>(r => r.Days == InsightPathService.WindowDays && r.End == DateTime.Today), 0),
            Times.Once);
    }

    // ---- the real engine, over actual days ------------------------------------------------

    private static DiaryEntryStatsDto Entry(int daysAgo, Emotion emotion, double? sleepHours = null)
    {
        var dto = new DiaryEntryStatsDto { Date = DateTime.Today.AddDays(-daysAgo), Emotion = emotion };
        if (sleepHours is { } hours)
        {
            dto.SleepStart = new TimeSpan(22, 0, 0);
            dto.SleepEnd = TimeSpan.FromHours((22 + hours) % 24);
        }

        return dto;
    }

    /// <summary>Mood that rises with sleep when sleep rises with the index — a correlation that holds.</summary>
    private static Emotion Rising(int i) => i switch
    {
        < 5 => Emotion.Sad,
        < 10 => Emotion.Anxious,
        _ => Emotion.Calm
    };

    /// <summary>Mood that has nothing to do with the index — a test that fails.</summary>
    private static Emotion Alternating(int i) => i % 2 == 0 ? Emotion.Happy : Emotion.Sad;

    private static IEnumerable<DiaryEntryStatsDto> MoodAndSleep(int count, Func<int, Emotion> mood, int firstDaysAgo = 0) =>
        Enumerable.Range(firstDaysAgo, count).Select(i => Entry(i, mood(i), 4 + i * 0.3));

    private static InsightPathService BuildWithRealEngine(
        IEnumerable<DiaryEntryStatsDto> data,
        IThemeClusterService? themes = null,
        IProfileService? profiles = null)
    {
        // Bounded the way DiaryService bounds it, inclusive on both calendar days, so the window the
        // service asks for is what the engine actually sees.
        var entries = data.ToList();
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync((DateTime start, DateTime end) =>
                 entries.Where(e => e.Date >= start.Date && e.Date <= end.Date));

        var cycle = new Mock<ICycleLogService>();
        cycle.Setup(s => s.GetLogsAsync()).ReturnsAsync(new List<CycleLog>());

        if (profiles is null)
        {
            var mock = new Mock<IProfileService>();
            mock.Setup(p => p.GetUserProfileAsync()).ReturnsAsync(new UserProfile());
            profiles = mock.Object;
        }

        // Tasks and spending are not what these tests are about, but Moq hands back a null Task
        // for an unconfigured async member, so both need an explicit empty result.
        var todos = new Mock<ITodoService>();
        todos.Setup(s => s.GetTodosForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(Enumerable.Empty<TodoStatsDto>());

        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync(new List<FinanceTransaction>());

        var engine = new CorrelationService(
            diary.Object, cycle.Object, profiles, todos.Object, finance.Object,
            themes ?? new NullThemeClusterService());

        return new InsightPathService(engine, profiles, new FixedEntitlementService(Trial));
    }

    [Fact]
    public async Task AC2_NineDaysOfMoodAndSleep_CountNine_AndNameSleep()
    {
        var state = await BuildWithRealEngine(MoodAndSleep(9, Rising)).GetAsync();

        state.IsVisible.Should().BeTrue();
        state.PairedDays.Should().Be(9);
        state.BestFactorKey.Should().Be(Factors.SleepDuration);
        state.ReachedMilestone.Should().Be(7);
        state.NextMilestone.Should().Be(14);
        state.FirstHypothesis.Should().BeNull();
    }

    [Fact]
    public async Task AC3_NineDaysOfMoodAlone_CountNothing()
    {
        var data = Enumerable.Range(0, 9).Select(i => Entry(i, Rising(i)));

        var state = await BuildWithRealEngine(data).GetAsync();

        state.IsVisible.Should().BeTrue("the card is what tells them why it says zero");
        state.PairedDays.Should().Be(0);
        state.BestFactorKey.Should().BeNull();
        state.ReachedMilestone.Should().Be(0);
        state.NextMilestone.Should().Be(1);
    }

    [Fact]
    public async Task AC4_FourteenPairedDaysWithARealCorrelation_ArriveAtTheHypothesis()
    {
        var state = await BuildWithRealEngine(MoodAndSleep(14, Rising)).GetAsync();

        state.IsVisible.Should().BeTrue();
        state.PairedDays.Should().Be(14);
        state.ReachedMilestone.Should().Be(14);
        state.FirstHypothesis.Should().NotBeNull();
        state.FirstHypothesis!.FactorKey.Should().Be(Factors.SleepDuration);
        state.FirstHypothesis.SampleSize.Should().Be(14);
        state.FirstHypothesis.Holds.Should().BeTrue();
    }

    [Fact]
    public async Task AC5_FourteenPairedDaysWhereSleepExplainsNothing_ShowNoCard()
    {
        var state = await BuildWithRealEngine(MoodAndSleep(14, Alternating)).GetAsync();

        state.PairedDays.Should().Be(14, "the days paired; the test on them simply failed");
        state.ReachedMilestone.Should().Be(14);
        state.FirstHypothesis.Should().BeNull();
        state.IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task AC7_ThreeMissedDays_DoNotMoveThePathBack()
    {
        var then = await BuildWithRealEngine(MoodAndSleep(9, Rising)).GetAsync();
        // The same nine days, seen three days later with nothing written since.
        var now = await BuildWithRealEngine(MoodAndSleep(9, Rising, firstDaysAgo: 3)).GetAsync();

        now.PairedDays.Should().Be(then.PairedDays).And.Be(9);
        now.ReachedMilestone.Should().Be(then.ReachedMilestone).And.Be(7);
        now.IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task DaysOutsideTheWindow_DoNotCount()
    {
        // Nine paired days, all older than the window: the path shows the last thirty days (D1).
        var state = await BuildWithRealEngine(MoodAndSleep(9, Rising, firstDaysAgo: InsightPathService.WindowDays)).GetAsync();

        state.PairedDays.Should().Be(0);
    }

    [Fact]
    public async Task AC9_TheReadinessPass_NeverAsksTheClusterService()
    {
        // Themes are kept out of the count on purpose (R1.1): whether today counted must not wait
        // on the encoder. The hypothesis check is the statistics screen's own pass and keeps them,
        // so one clustering per GetAsync is the floor — and the readiness pass adds none.
        var themes = new Mock<IThemeClusterService>();
        themes.Setup(t => t.SummariseAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ThemeSummary([], []));

        await BuildWithRealEngine(MoodAndSleep(9, Rising), themes.Object).GetAsync();

        themes.Verify(
            t => t.SummariseAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Hiding_SurvivesARoundTripThroughTheDatabase()
    {
        // The profile field is new and has no migration; this is the proof it is actually stored.
        using var db = new DatabaseContext(useInMemory: true);
        var service = BuildWithRealEngine(MoodAndSleep(5, Rising), profiles: new ProfileService(db));

        await service.HideUntilNextMilestoneAsync();

        var reloaded = await new ProfileService(db).GetUserProfileAsync();
        reloaded.InsightPathHiddenBelowMilestone.Should().Be(7, "five paired days stand at the third milestone");
        (await service.GetAsync()).IsVisible.Should().BeFalse();
    }
}
