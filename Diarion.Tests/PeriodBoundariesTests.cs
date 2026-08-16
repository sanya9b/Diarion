using System;
using System.Globalization;
using Diarion.Models;
using Diarion.Models.Ai.Reports;
using Diarion.Services.Ai.Reports;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class PeriodBoundariesTests
{
    [Theory]
    // Every day of one week lands on the same Monday-to-Sunday window.
    [InlineData(2026, 8, 10, "2026-08-10", "2026-08-16")] // Monday itself
    [InlineData(2026, 8, 13, "2026-08-10", "2026-08-16")] // midweek
    [InlineData(2026, 8, 16, "2026-08-10", "2026-08-16")] // Sunday, the trap for a Sunday-first calendar
    public void Week_starts_on_monday(int y, int m, int d, string start, string end)
    {
        var range = PeriodBoundaries.Containing(PeriodKind.Week, new DateTime(y, m, d));

        Iso(range).Should().Be((start, end));
    }

    [Fact]
    public void Week_spans_the_new_year()
    {
        // 1 January 2027 is a Friday, so its week begins in the previous year.
        var range = PeriodBoundaries.Containing(PeriodKind.Week, new DateTime(2027, 1, 1));

        Iso(range).Should().Be(("2026-12-28", "2027-01-03"));
    }

    [Fact]
    public void Month_ends_on_the_last_day_even_in_february()
    {
        Iso(PeriodBoundaries.Containing(PeriodKind.Month, new DateTime(2028, 2, 14)))
            .Should().Be(("2028-02-01", "2028-02-29"));

        Iso(PeriodBoundaries.Containing(PeriodKind.Month, new DateTime(2026, 2, 14)))
            .Should().Be(("2026-02-01", "2026-02-28"));
    }

    [Theory]
    [InlineData(1, "2026-01-01", "2026-03-31")]
    [InlineData(3, "2026-01-01", "2026-03-31")]
    [InlineData(4, "2026-04-01", "2026-06-30")]
    [InlineData(8, "2026-07-01", "2026-09-30")]
    [InlineData(12, "2026-10-01", "2026-12-31")]
    public void Quarter_covers_three_calendar_months(int month, string start, string end)
    {
        Iso(PeriodBoundaries.Containing(PeriodKind.Quarter, new DateTime(2026, month, 20)))
            .Should().Be((start, end));
    }

    [Fact]
    public void Year_covers_the_calendar_year()
    {
        Iso(PeriodBoundaries.Containing(PeriodKind.Year, new DateTime(2026, 8, 10)))
            .Should().Be(("2026-01-01", "2026-12-31"));
    }

    [Fact]
    public void Containing_ignores_the_time_of_day()
    {
        var atNight = PeriodBoundaries.Containing(PeriodKind.Week, new DateTime(2026, 8, 13, 23, 59, 0));

        Iso(atNight).Should().Be(("2026-08-10", "2026-08-16"));
    }

    [Fact]
    public void LastClosed_week_is_the_one_before_this_one()
    {
        // Tuesday. The report offered must be last week, not the two days lived so far.
        Iso(PeriodBoundaries.LastClosed(PeriodKind.Week, new DateTime(2026, 8, 11)))
            .Should().Be(("2026-08-03", "2026-08-09"));
    }

    [Fact]
    public void LastClosed_week_on_a_monday_is_the_week_that_ended_yesterday()
    {
        Iso(PeriodBoundaries.LastClosed(PeriodKind.Week, new DateTime(2026, 8, 10)))
            .Should().Be(("2026-08-03", "2026-08-09"));
    }

    [Fact]
    public void LastClosed_month_and_quarter_and_year_step_back_across_the_boundary()
    {
        var newYearsDay = new DateTime(2027, 1, 1);

        Iso(PeriodBoundaries.LastClosed(PeriodKind.Month, newYearsDay)).Should().Be(("2026-12-01", "2026-12-31"));
        Iso(PeriodBoundaries.LastClosed(PeriodKind.Quarter, newYearsDay)).Should().Be(("2026-10-01", "2026-12-31"));
        Iso(PeriodBoundaries.LastClosed(PeriodKind.Year, newYearsDay)).Should().Be(("2026-01-01", "2026-12-31"));
    }

    [Fact]
    public void IsClosed_is_false_until_the_last_day_is_behind_us()
    {
        var week = PeriodBoundaries.Containing(PeriodKind.Week, new DateTime(2026, 8, 11));

        PeriodBoundaries.IsClosed(week, new DateTime(2026, 8, 11)).Should().BeFalse();

        // The final Sunday is still today — the day is not over, so neither is the week.
        PeriodBoundaries.IsClosed(week, new DateTime(2026, 8, 16)).Should().BeFalse();

        PeriodBoundaries.IsClosed(week, new DateTime(2026, 8, 17)).Should().BeTrue();
    }

    [Fact]
    public void LastClosed_is_always_closed()
    {
        var today = new DateTime(2026, 8, 11);

        foreach (PeriodKind kind in Enum.GetValues<PeriodKind>())
        {
            PeriodBoundaries.IsClosed(PeriodBoundaries.LastClosed(kind, today), today)
                .Should().BeTrue($"{kind} must never offer a period that is still being lived");
        }
    }

    [Fact]
    public void Unknown_kind_is_rejected_rather_than_silently_treated_as_a_week()
    {
        var act = () => PeriodBoundaries.Containing((PeriodKind)99, new DateTime(2026, 8, 10));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static (string Start, string End) Iso(StatsRange range)
        => (range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
