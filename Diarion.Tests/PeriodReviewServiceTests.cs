using System;
using System.Threading;
using System.Threading.Tasks;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Services.Ai.Reports;
using Diarion.Services.Reports;
using FluentAssertions;
using Moq;
using Xunit;

namespace Diarion.Tests;

public class PeriodReviewServiceTests
{
    [Fact]
    public void The_previous_window_is_the_same_length_and_ends_the_day_before()
    {
        var week = new StatsRange(new DateTime(2026, 10, 5), new DateTime(2026, 10, 11));

        var before = PeriodReviewService.PreviousOf(week);

        before.Start.Should().Be(new DateTime(2026, 9, 28));
        before.End.Should().Be(new DateTime(2026, 10, 4));
        before.Days.Should().Be(7);
    }

    [Fact]
    public void The_previous_window_of_a_month_to_date_is_the_same_number_of_days_not_the_calendar_month()
    {
        // Month-to-date on the 10th is ten days; comparing it with all of last month would set ten
        // days of spending against thirty-one and call it a saving.
        var toDate = new StatsRange(new DateTime(2026, 10, 1), new DateTime(2026, 10, 10));

        var before = PeriodReviewService.PreviousOf(toDate);

        before.Should().Be(new StatsRange(new DateTime(2026, 9, 21), new DateTime(2026, 9, 30)));
    }

    [Fact]
    public void An_inverted_range_is_put_in_order_first()
    {
        var inverted = new StatsRange(new DateTime(2026, 10, 11), new DateTime(2026, 10, 5));

        PeriodReviewService.PreviousOf(inverted)
            .Should().Be(new StatsRange(new DateTime(2026, 9, 28), new DateTime(2026, 10, 4)));
    }

    [Theory]
    [InlineData(1, PeriodKind.Week)]
    [InlineData(7, PeriodKind.Week)]
    [InlineData(8, PeriodKind.Month)]
    [InlineData(31, PeriodKind.Month)]
    [InlineData(32, PeriodKind.Quarter)]
    [InlineData(92, PeriodKind.Quarter)]
    [InlineData(93, PeriodKind.Year)]
    [InlineData(365, PeriodKind.Year)]
    public void The_snapshot_is_labelled_by_the_nearest_cadence(int days, PeriodKind expected)
    {
        var range = new StatsRange(new DateTime(2026, 1, 1), new DateTime(2026, 1, 1).AddDays(days - 1));

        PeriodReviewService.KindFor(range).Should().Be(expected);
    }

    [Fact]
    public async Task Build_asks_for_both_windows_with_the_default_options_and_compares_them()
    {
        var week = new StatsRange(new DateTime(2026, 10, 5), new DateTime(2026, 10, 11));
        var before = PeriodReviewService.PreviousOf(week);

        var builder = new Mock<ISnapshotBuilder>(MockBehavior.Strict);
        builder.Setup(b => b.BuildAsync(PeriodKind.Week, week, SnapshotOptions.Default, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new PeriodSnapshot { Tasks = new SnapshotTasks { Completed = 2, Total = 3 } });
        builder.Setup(b => b.BuildAsync(PeriodKind.Week, before, SnapshotOptions.Default, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new PeriodSnapshot { Tasks = new SnapshotTasks { Completed = 1, Total = 4 } });

        var review = await new PeriodReviewService(builder.Object).BuildAsync(week);

        review.Range.Should().Be(week);
        review.PreviousRange.Should().Be(before);
        review.TasksCompleted.Delta.Should().Be(1);
        builder.VerifyAll();
    }
}
