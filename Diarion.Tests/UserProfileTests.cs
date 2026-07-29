using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class UserProfileTests
{
    [Fact]
    public void GetEffectiveStreakGrace_ReturnsZeroWhenDisabled()
    {
        var profile = new UserProfile { IsForgivingStreaksEnabled = false, StreakGraceDays = 3 };

        profile.GetEffectiveStreakGrace().Should().Be(0);
    }

    [Fact]
    public void GetEffectiveStreakGrace_ClampsAboveMax()
    {
        var profile = new UserProfile { IsForgivingStreaksEnabled = true, StreakGraceDays = 99 };

        profile.GetEffectiveStreakGrace().Should().Be(UserProfile.MaxStreakGraceDays);
    }

    [Fact]
    public void NormalizeStreakSettings_ClampsAndReportsTheChange()
    {
        var profile = new UserProfile { StreakGraceDays = 99 };

        profile.NormalizeStreakSettings().Should().BeTrue();
        profile.StreakGraceDays.Should().Be(UserProfile.MaxStreakGraceDays);
        profile.NormalizeStreakSettings().Should().BeFalse();
    }
}
