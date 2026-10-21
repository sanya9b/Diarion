using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diarion.Resources.Localization;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The "days with entries" card on the statistics screen: every day of the window, filled where something
/// was written. It took over from the strip under the date as the place the days themselves are seen.
/// </summary>
public class StatisticsActivityTests
{
    private static readonly DateTime Start = new(2026, 3, 1);
    private static readonly DateTime End = new(2026, 3, 7);

    [Fact]
    public async Task ActivityCard_FillsTheWrittenDaysOfTheWindow()
    {
        var vm = StatisticsRangeTests.BuildViewModel(diary => diary
            .Setup(d => d.GetContentDatesAsync(Start, End))
            .ReturnsAsync(new List<DateTime> { Start, Start.AddDays(3) }));
        vm.RangeStart = Start;
        vm.RangeEnd = End;

        await vm.LoadStatisticsAsync();

        vm.ActivityDates.Should().Equal(Start, Start.AddDays(3));
        vm.ActivityCountText.Should().Be(string.Format(AppResources.StatsActivityCountFormat, 2, 7));
        vm.HasActivityDays.Should().BeTrue();
    }

    [Fact]
    public async Task ActivityCard_WithNothingWritten_StillShowsTheWindow()
    {
        // An empty window is information too — the squares are there, none of them filled.
        var vm = StatisticsRangeTests.BuildViewModel(diary => diary
            .Setup(d => d.GetContentDatesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<DateTime>()));
        vm.RangeStart = Start;
        vm.RangeEnd = End;

        await vm.LoadStatisticsAsync();

        vm.ActivityDates.Should().BeEmpty();
        vm.HasActivityDays.Should().BeTrue("the squares still show the window");
        vm.ActivityCountText.Should().Be(string.Format(AppResources.StatsActivityCountFormat, 0, 7));
    }
}
