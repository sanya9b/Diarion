using System;
using System.Collections.Generic;
using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// Two gates on the profile since 2026-09-20: gender (unchanged) and age, which needs a birth date.
/// Dates here sit years away from any boundary — the boundaries themselves are AgeGateTests' job —
/// so the suite does not start failing on someone's birthday.
/// </summary>
public class UserProfileCycleAvailabilityTests
{
    private static readonly DateTime Adult = DateTime.Today.AddYears(-30);
    private static readonly DateTime Teen = DateTime.Today.AddYears(-16);
    private static readonly DateTime Child = DateTime.Today.AddYears(-10);

    [Fact]
    public void Male_MakesTheFeatureUnavailableEvenWhenItWasSwitchedOn()
    {
        var profile = new UserProfile { IsMenstrualTrackingEnabled = true, Gender = GenderType.Male, BirthDate = Adult };

        profile.IsCycleFeatureAvailable.Should().BeFalse();
        profile.IsCycleTrackingActive.Should().BeFalse();

        // The stored preference is left alone, so changing gender back restores it.
        profile.IsMenstrualTrackingEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(GenderType.Female)]
    [InlineData(GenderType.Other)]
    [InlineData(GenderType.NotSpecified)]
    public void EveryOtherGender_KeepsTheFeatureAvailable_OnceOldEnough(GenderType gender)
    {
        var profile = new UserProfile { IsMenstrualTrackingEnabled = true, Gender = gender, BirthDate = Adult };

        profile.IsCycleFeatureAvailable.Should().BeTrue();
        profile.IsCycleTrackingActive.Should().BeTrue();
    }

    [Fact]
    public void AvailableButNotEnabled_IsNotActive()
    {
        new UserProfile { Gender = GenderType.Female, BirthDate = Adult }.IsCycleTrackingActive.Should().BeFalse();
    }

    [Fact]
    public void NoBirthDate_HidesTheCycleEvenForAWomanWhoSwitchedItOn()
    {
        var profile = new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = true };

        profile.IsCycleFeatureAvailable.Should().BeFalse();
        profile.IsCycleTrackingActive.Should().BeFalse();
        profile.NeedsBirthDateForCycle.Should().BeTrue("the settings card should say why the feature is missing");

        // Nothing is thrown away: telling the date brings it straight back.
        profile.BirthDate = Adult;
        profile.IsCycleTrackingActive.Should().BeTrue();
        profile.NeedsBirthDateForCycle.Should().BeFalse();
    }

    [Fact]
    public void Under14_HidesTheCycle_AndDoesNotAskForTheDateAgain()
    {
        var profile = new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = true, BirthDate = Child };

        profile.IsCycleFeatureAvailable.Should().BeFalse();
        profile.NeedsBirthDateForCycle.Should().BeFalse("the date is known; there is nothing more to ask");
        profile.IsMenstrualTrackingEnabled.Should().BeTrue("the preference survives until the age is reached");
    }

    [Fact]
    public void Sixteen_IsOldEnoughForTheCycle_ButNotForIntimateLife()
    {
        var profile = new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = true, BirthDate = Teen };

        profile.IsCycleTrackingActive.Should().BeTrue();
        profile.IsIntimateLifeAvailable.Should().BeFalse();
        profile.ShowsIntimateLifeAgeHint.Should().BeFalse("a known-and-too-young profile is told nothing");
    }

    [Fact]
    public void Men_DoNotSeeTheCycleHint()
    {
        new UserProfile { Gender = GenderType.Male }.NeedsBirthDateForCycle.Should().BeFalse();
    }

    [Fact]
    public void IntimateLife_NeedsAKnownAdultDate()
    {
        new UserProfile().IsIntimateLifeAvailable.Should().BeFalse();
        new UserProfile().ShowsIntimateLifeAgeHint.Should().BeTrue("the row's place is taken by the hint until the date is told");

        new UserProfile { BirthDate = Adult }.IsIntimateLifeAvailable.Should().BeTrue();
        new UserProfile { BirthDate = Adult }.ShowsIntimateLifeAgeHint.Should().BeFalse();

        // Gender plays no part in this gate.
        new UserProfile { BirthDate = Adult, Gender = GenderType.Male }.IsIntimateLifeAvailable.Should().BeTrue();
    }

    [Fact]
    public void ChangingGender_NotifiesBothComputedProperties()
    {
        var profile = new UserProfile { IsMenstrualTrackingEnabled = true, BirthDate = Adult };
        var changed = new List<string?>();
        profile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        profile.Gender = GenderType.Male;

        changed.Should().Contain(nameof(UserProfile.IsCycleFeatureAvailable));
        changed.Should().Contain(nameof(UserProfile.IsCycleTrackingActive));
        changed.Should().Contain(nameof(UserProfile.NeedsBirthDateForCycle));
    }

    [Fact]
    public void ChangingBirthDate_NotifiesEveryGate()
    {
        var profile = new UserProfile { Gender = GenderType.Female, IsMenstrualTrackingEnabled = true };
        var changed = new List<string?>();
        profile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        profile.BirthDate = Adult;

        changed.Should().Contain(new[]
        {
            nameof(UserProfile.IsBirthDateKnown),
            nameof(UserProfile.IsIntimateLifeAvailable),
            nameof(UserProfile.ShowsIntimateLifeAgeHint),
            nameof(UserProfile.IsCycleFeatureAvailable),
            nameof(UserProfile.IsCycleTrackingActive),
            nameof(UserProfile.NeedsBirthDateForCycle)
        });
    }

    [Fact]
    public void TogglingTracking_NotifiesTheActiveProperty()
    {
        var profile = new UserProfile { Gender = GenderType.Female, BirthDate = Adult };
        var changed = new List<string?>();
        profile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        profile.IsMenstrualTrackingEnabled = true;

        changed.Should().Contain(nameof(UserProfile.IsCycleTrackingActive));
    }
}
