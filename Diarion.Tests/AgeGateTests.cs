using System;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The gate is a pure function of two dates, so every boundary is pinned to fixed dates here rather
/// than to whatever day the suite happens to run on.
/// </summary>
public class AgeGateTests
{
    private static readonly DateTime Today = new(2026, 9, 20);

    [Fact]
    public void Unknown_birth_date_fails_every_threshold()
    {
        AgeGate.YearsOld(null, Today).Should().BeNull();
        AgeGate.IsAtLeast(null, 0, Today).Should().BeFalse();
    }

    [Fact]
    public void Turns_a_year_older_on_the_birthday_itself_not_the_day_before()
    {
        var birth = new DateTime(2008, 9, 20);

        AgeGate.YearsOld(birth, Today).Should().Be(18);
        AgeGate.IsAtLeast(birth, 18, Today).Should().BeTrue();

        AgeGate.YearsOld(birth, Today.AddDays(-1)).Should().Be(17);
        AgeGate.IsAtLeast(birth, 18, Today.AddDays(-1)).Should().BeFalse();
    }

    [Fact]
    public void Time_of_day_does_not_count()
    {
        var birth = new DateTime(2008, 9, 20, 23, 59, 0);

        AgeGate.YearsOld(birth, new DateTime(2026, 9, 20, 0, 0, 1)).Should().Be(18);
    }

    [Fact]
    public void A_leap_day_birthday_counts_from_28_february_in_common_years()
    {
        var birth = new DateTime(2012, 2, 29);

        AgeGate.YearsOld(birth, new DateTime(2026, 2, 27)).Should().Be(13);
        AgeGate.YearsOld(birth, new DateTime(2026, 2, 28)).Should().Be(14);
        AgeGate.YearsOld(birth, new DateTime(2028, 2, 29)).Should().Be(16);
    }

    [Fact]
    public void A_birth_date_in_the_future_is_treated_as_unknown()
    {
        AgeGate.YearsOld(Today.AddDays(1), Today).Should().BeNull();
        AgeGate.IsAtLeast(Today.AddDays(1), 0, Today).Should().BeFalse();
    }

    [Fact]
    public void The_thresholds_are_the_two_the_product_decided_on()
    {
        AgeGate.IntimateLifeMinimumAge.Should().Be(18);
        AgeGate.CycleMinimumAge.Should().Be(14);
    }
}
