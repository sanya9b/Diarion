using System;
using Diarion.Services.Review;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The rating request is a pure function of a count and two dates, so every boundary is pinned to
/// fixed dates here rather than to whatever day the suite happens to run on.
/// </summary>
public class ReviewPromptPolicyTests
{
    private static readonly DateTime Today = new(2026, 11, 13);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public void Fewer_than_seven_days_with_entries_never_asks(int days)
    {
        ReviewPromptPolicy.ShouldPrompt(days, null, Today).Should().BeFalse();
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(400)]
    public void Seven_or_more_days_ask_when_never_asked_before(int days)
    {
        ReviewPromptPolicy.ShouldPrompt(days, null, Today).Should().BeTrue();
    }

    [Fact]
    public void A_request_today_blocks_the_next_one_for_the_whole_cooldown()
    {
        var asked = Today;

        ReviewPromptPolicy.ShouldPrompt(50, asked, Today).Should().BeFalse();
        ReviewPromptPolicy.ShouldPrompt(50, asked, Today.AddDays(ReviewPromptPolicy.CooldownDays - 1)).Should().BeFalse();
        ReviewPromptPolicy.ShouldPrompt(50, asked, Today.AddDays(ReviewPromptPolicy.CooldownDays)).Should().BeTrue();
    }

    [Fact]
    public void Cooldown_compares_dates_not_times()
    {
        var askedLateEvening = Today.AddDays(-ReviewPromptPolicy.CooldownDays).AddHours(23).AddMinutes(59);

        ReviewPromptPolicy.CooldownAllows(askedLateEvening, Today).Should().BeTrue();
    }

    [Fact]
    public void Constants_are_the_ones_the_plan_promises()
    {
        ReviewPromptPolicy.MinDaysWithEntries.Should().Be(7);
        ReviewPromptPolicy.CooldownDays.Should().Be(120);
    }
}
