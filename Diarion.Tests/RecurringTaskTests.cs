using System;
using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// <see cref="RecurringTask.IsActiveOn"/> is the one answer to "does this row still repeat", and the list
/// and the edit form now both read it. When they each answered it their own way, one of them went on
/// showing a repeat the user had already switched off.
/// </summary>
public class RecurringTaskTests
{
    private static readonly DateTime Day = new(2026, 7, 30);

    private static RecurringTask EndingOn(DateTime? end)
        => new() { Recurrence = new RecurrenceRule { Kind = RecurrenceKind.Daily, Anchor = Day.AddDays(-10), EndDate = end } };

    [Fact]
    public void ASeriesWithNoEndDateIsActiveOnAnyDay()
        => EndingOn(null).IsActiveOn(Day).Should().BeTrue();

    [Fact]
    public void TheLastDayOfASeriesStillCounts()
        => EndingOn(Day).IsActiveOn(Day).Should().BeTrue("a series ending on a day still runs on it");

    [Fact]
    public void TheDayAfterTheEndDateDoesNot()
        => EndingOn(Day).IsActiveOn(Day.AddDays(1)).Should().BeFalse();

    [Fact]
    public void ADayBeforeTheEndIsStillActive()
    {
        // Which is why unticking Repeat on Wednesday leaves Monday's occurrence still marked as repeating:
        // on Monday the series genuinely was running. The list and the form agree on that.
        EndingOn(Day).IsActiveOn(Day.AddDays(-2)).Should().BeTrue();
    }

    [Fact]
    public void TheTimeOfDayIsIgnored()
        => EndingOn(Day).IsActiveOn(Day.AddHours(23)).Should().BeTrue();

    [Fact]
    public void ARuleThatCameBackWithoutItsRecurrenceIsTreatedAsRunning()
    {
        // LiteDB can hand back a missing sub-document as null. Reading through it would throw on a day
        // load, which is a worse failure than showing one glyph too many.
        new RecurringTask { Recurrence = null! }.IsActiveOn(Day).Should().BeTrue();
    }
}
