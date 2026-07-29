using System.Collections.Generic;
using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class UserProfileCycleAvailabilityTests
{
    [Fact]
    public void Male_MakesTheFeatureUnavailableEvenWhenItWasSwitchedOn()
    {
        var profile = new UserProfile { IsMenstrualTrackingEnabled = true, Gender = GenderType.Male };

        profile.IsCycleFeatureAvailable.Should().BeFalse();
        profile.IsCycleTrackingActive.Should().BeFalse();

        // The stored preference is left alone, so changing gender back restores it.
        profile.IsMenstrualTrackingEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(GenderType.Female)]
    [InlineData(GenderType.Other)]
    [InlineData(GenderType.NotSpecified)]
    public void EveryOtherGender_KeepsTheFeatureAvailable(GenderType gender)
    {
        var profile = new UserProfile { IsMenstrualTrackingEnabled = true, Gender = gender };

        profile.IsCycleFeatureAvailable.Should().BeTrue();
        profile.IsCycleTrackingActive.Should().BeTrue();
    }

    [Fact]
    public void AvailableButNotEnabled_IsNotActive()
    {
        new UserProfile { Gender = GenderType.Female }.IsCycleTrackingActive.Should().BeFalse();
    }

    [Fact]
    public void ChangingGender_NotifiesBothComputedProperties()
    {
        var profile = new UserProfile { IsMenstrualTrackingEnabled = true };
        var changed = new List<string?>();
        profile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        profile.Gender = GenderType.Male;

        changed.Should().Contain(nameof(UserProfile.IsCycleFeatureAvailable));
        changed.Should().Contain(nameof(UserProfile.IsCycleTrackingActive));
    }

    [Fact]
    public void TogglingTracking_NotifiesTheActiveProperty()
    {
        var profile = new UserProfile { Gender = GenderType.Female };
        var changed = new List<string?>();
        profile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        profile.IsMenstrualTrackingEnabled = true;

        changed.Should().Contain(nameof(UserProfile.IsCycleTrackingActive));
    }
}
