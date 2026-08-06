using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Services;
using Diarion.Services.Ai;
using Diarion.ViewModels.Statistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>The cycle reaching the three places the roadmap said it never did: the statistics tab,
/// the correlation factors, and the Markdown export.</summary>
public class CycleStatsViewModelTests
{
    private static (CycleStatsViewModel Vm, Mock<IProfileService> Profile) Build(
        IEnumerable<DateTime>? marked = null,
        IEnumerable<CycleLog>? logs = null,
        GenderType gender = GenderType.Female,
        bool trackingEnabled = true)
    {
        var cycle = new Mock<ICycleLogService>();
        cycle.Setup(s => s.GetMarkedDatesAsync()).ReturnsAsync((marked ?? Enumerable.Empty<DateTime>()).ToList());
        cycle.Setup(s => s.GetLogsAsync()).ReturnsAsync((logs ?? Enumerable.Empty<CycleLog>()).ToList());

        var profile = new Mock<IProfileService>();
        profile.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(new UserProfile
        {
            Gender = gender,
            IsMenstrualTrackingEnabled = trackingEnabled
        });

        return (new CycleStatsViewModel(cycle.Object, profile.Object), profile);
    }

    private static List<DateTime> RegularMarkedDays()
    {
        var days = new List<DateTime>();
        foreach (var offset in new[] { 84, 56, 28 })
        {
            var start = DateTime.Today.AddDays(-offset);
            for (int i = 0; i < 5; i++) days.Add(start.AddDays(i));
        }
        return days;
    }

    [Fact]
    public async Task IsAvailable_FollowsTheSameGateAsTheRestOfTheFeature()
    {
        var (female, _) = Build();
        (await female.IsAvailableAsync()).Should().BeTrue();

        var (male, _) = Build(gender: GenderType.Male);
        (await male.IsAvailableAsync()).Should().BeFalse();

        var (disabled, _) = Build(trackingEnabled: false);
        (await disabled.IsAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task LoadData_FillsTilesChartAndCalendar()
    {
        var (vm, _) = Build(RegularMarkedDays());

        await vm.LoadDataAsync(7);   // the range is deliberately ignored by this tab

        vm.IsEmpty.Should().BeFalse();
        vm.AverageCycleText.Should().Be("28");
        vm.ShortestCycleText.Should().Be("28");
        vm.LongestCycleText.Should().Be("28");
        vm.CycleChartData.Should().HaveCount(2);
        vm.HasCycles.Should().BeTrue();
        vm.HasCalendar.Should().BeTrue();
        vm.BasisText.Should().NotBeNullOrEmpty("the card must always name what the numbers stand on");
    }

    [Fact]
    public async Task LoadData_WithOneEpisode_SaysThereIsNoCompleteCycleYet()
    {
        var start = DateTime.Today.AddDays(-30);
        var (vm, _) = Build(Enumerable.Range(0, 4).Select(i => start.AddDays(i)).ToList());

        await vm.LoadDataAsync(30);

        vm.IsEmpty.Should().BeFalse();
        vm.AverageCycleText.Should().Be("—");
        vm.HasCycles.Should().BeFalse();
        vm.BasisText.Should().Be(Diarion.Resources.Localization.AppResources.CycleStatsNoCompleteCycle);
    }

    [Fact]
    public async Task LoadData_ResolvesSymptomLabelsFromKeys()
    {
        var logs = new List<CycleLog>
        {
            new() { Date = DateTime.Today.AddDays(-2), Symptoms = new List<string> { CycleSymptoms.Cramps } },
            new() { Date = DateTime.Today.AddDays(-1), Symptoms = new List<string> { CycleSymptoms.Cramps } }
        };

        var (vm, _) = Build(RegularMarkedDays(), logs);
        await vm.LoadDataAsync(90);

        vm.HasSymptoms.Should().BeTrue();
        vm.Symptoms[0].CountText.Should().Be("2");
        vm.Symptoms[0].Label.Should().NotBe(CycleSymptoms.Cramps, "the key must be resolved to a label");
    }

    [Fact]
    public async Task LoadData_WithNothingLogged_IsEmpty()
    {
        var (vm, _) = Build();

        await vm.LoadDataAsync(365);

        vm.IsEmpty.Should().BeTrue();
        vm.HasCycles.Should().BeFalse();
        vm.HasSymptoms.Should().BeFalse();
        vm.HasCalendar.Should().BeFalse();
    }
}

public class CycleCorrelationTests
{
    private static CorrelationService Build(
        List<DiaryEntryStatsDto> entries,
        List<CycleLog> logs,
        bool trackingEnabled = true)
    {
        var diary = new Mock<IDiaryService>();
        diary.Setup(s => s.GetDiaryEntriesForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(entries);

        var cycle = new Mock<ICycleLogService>();
        cycle.Setup(s => s.GetLogsAsync()).ReturnsAsync(logs);

        var profile = new Mock<IProfileService>();
        profile.Setup(s => s.GetUserProfileAsync()).ReturnsAsync(new UserProfile
        {
            Gender = GenderType.Female,
            IsMenstrualTrackingEnabled = trackingEnabled
        });

        // Tasks and spending are not what these tests are about, but Moq hands back a null Task
        // for an unconfigured async member, so both need an explicit empty result.
        var todos = new Mock<ITodoService>();
        todos.Setup(s => s.GetTodosForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
             .ReturnsAsync(Enumerable.Empty<TodoStatsDto>());

        var finance = new Mock<IFinanceService>();
        finance.Setup(s => s.GetFinanceTransactionsForStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync(new List<FinanceTransaction>());

        return new CorrelationService(
            diary.Object, cycle.Object, profile.Object, todos.Object, finance.Object,
            new NullThemeClusterService());
    }

    /// <summary>Thirty days where mood is low exactly on the five logged period days.</summary>
    private static (List<DiaryEntryStatsDto> Entries, List<CycleLog> Logs) LowMoodOnPeriodDays()
    {
        var entries = new List<DiaryEntryStatsDto>();
        var logs = new List<CycleLog>();

        for (int i = 0; i < 30; i++)
        {
            var date = DateTime.Today.AddDays(-i);
            bool isPeriod = i is >= 10 and < 15;

            entries.Add(new DiaryEntryStatsDto
            {
                Date = date,
                Emotion = isPeriod ? Emotion.Sad : Emotion.Happy
            });

            if (isPeriod)
            {
                logs.Add(new CycleLog { Date = date, IsSymptomOnly = false });
            }
        }

        return (entries, logs);
    }

    [Fact]
    public async Task PeriodDays_AppearAsAFactorAndCorrelateWithMood()
    {
        var (entries, logs) = LowMoodOnPeriodDays();
        var service = Build(entries, logs);

        var results = await service.GetMoodCorrelationsAsync(30);

        var periodFactor = results.FirstOrDefault(r => r.FactorKey == "CyclePeriodDay");
        periodFactor.Should().NotBeNull("the cycle is now one of the factors");
        periodFactor!.Coefficient.Should().BeLessThan(0, "mood was low exactly on the period days");
        periodFactor.SampleSize.Should().BeGreaterThanOrEqualTo(CorrelationService.MinSampleSize);
    }

    [Fact]
    public async Task CycleFactors_AreAbsentWhenTrackingIsOff()
    {
        var (entries, logs) = LowMoodOnPeriodDays();
        var service = Build(entries, logs, trackingEnabled: false);

        var results = await service.GetMoodCorrelationsAsync(30);

        results.Should().NotContain(r => r.FactorKey.StartsWith("Cycle"),
            "a factor the user is not tracking must not be computed at all");
    }

    [Fact]
    public async Task CycleFactors_AreAbsentWithNoLog()
    {
        var (entries, _) = LowMoodOnPeriodDays();
        var service = Build(entries, new List<CycleLog>());

        var results = await service.GetMoodCorrelationsAsync(30);

        results.Should().NotContain(r => r.FactorKey.StartsWith("Cycle"));
    }
}
