using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class MoodAggregateTests
{
    private static List<HourMood> Hours(params (int Hour, Emotion Mood)[] entries) =>
        entries.Select(e => new HourMood { Hour = e.Hour, Mood = e.Mood }).ToList();

    [Fact]
    public void Observations_HourlyWins_WhenPresent()
    {
        var hourly = Hours((9, Emotion.Sad), (14, Emotion.Angry));

        MoodAggregate.Observations(Emotion.Happy, hourly)
            .Should().Equal(Emotion.Sad, Emotion.Angry);
    }

    [Fact]
    public void Observations_FallsBackToScalar_WhenHourlyIsEmptyOrNull()
    {
        MoodAggregate.Observations(Emotion.Calm, new List<HourMood>()).Should().Equal(Emotion.Calm);
        MoodAggregate.Observations(Emotion.Calm, null).Should().Equal(Emotion.Calm);
    }

    [Fact]
    public void Observations_FallsBackToScalar_WhenEveryHourIsUnset()
    {
        // Materialised-but-untouched hour slots must not shadow a real day-level answer.
        var hourly = Hours((9, Emotion.None), (10, Emotion.None));

        MoodAggregate.Observations(Emotion.Happy, hourly).Should().Equal(Emotion.Happy);
    }

    [Fact]
    public void Observations_SkipsUnsetHours_WhenSomeAreSet()
    {
        var hourly = Hours((9, Emotion.None), (14, Emotion.Sad), (20, Emotion.None));

        MoodAggregate.Observations(Emotion.Happy, hourly).Should().Equal(Emotion.Sad);
    }

    [Fact]
    public void Observations_NothingRecorded_IsEmpty()
    {
        MoodAggregate.Observations(Emotion.None, new List<HourMood>()).Should().BeEmpty();
        MoodAggregate.HasAny(Emotion.None, null).Should().BeFalse();
    }

    [Fact]
    public void Valence_AveragesAcrossHours()
    {
        var hourly = Hours((9, Emotion.Happy), (14, Emotion.Sad));

        // +2 and -2
        MoodAggregate.Valence(Emotion.Calm, hourly).Should().Be(0);
    }

    [Fact]
    public void Valence_ScalarOnly_MatchesTheScalarValence()
    {
        MoodAggregate.Valence(Emotion.Sad, null).Should().Be(Emotion.Sad.ToValence());
    }

    [Fact]
    public void Valence_NothingRecorded_IsZero()
    {
        MoodAggregate.Valence(Emotion.None, null).Should().Be(0);
    }

    [Fact]
    public void Dominant_IsTheMostFrequentEmotion()
    {
        var hourly = Hours((9, Emotion.Sad), (10, Emotion.Sad), (11, Emotion.Happy));

        MoodAggregate.Dominant(Emotion.Calm, hourly).Should().Be(Emotion.Sad);
    }

    [Fact]
    public void Dominant_TieBreaksByEnumOrder_Deterministically()
    {
        var hourly = Hours((9, Emotion.Angry), (10, Emotion.Calm));

        var first = MoodAggregate.Dominant(Emotion.None, hourly);
        var second = MoodAggregate.Dominant(Emotion.None, Hours((10, Emotion.Calm), (9, Emotion.Angry)));

        first.Should().Be(second, "input order must not change the answer");
        ((int)first).Should().Be(new[] { (int)Emotion.Angry, (int)Emotion.Calm }.Min());
    }

    [Fact]
    public void Dominant_ScalarOnly_IsTheScalar()
    {
        // This is what keeps existing statistics byte-identical for days with no hourly data.
        MoodAggregate.Dominant(Emotion.Anxious, new List<HourMood>()).Should().Be(Emotion.Anxious);
    }

    [Fact]
    public void Dominant_NothingRecorded_IsNone()
    {
        MoodAggregate.Dominant(Emotion.None, null).Should().Be(Emotion.None);
    }

    [Fact]
    public void HourlyObservations_ScalarOnly_ReturnsEmpty()
    {
        MoodAggregate.HourlyObservations(new List<HourMood>()).Should().BeEmpty();
        MoodAggregate.HourlyObservations(null).Should().BeEmpty();
    }

    [Fact]
    public void HourlyObservations_DropsNoneSlots_AndKeepsHours()
    {
        var hourly = Hours((9, Emotion.None), (3, Emotion.Happy), (14, Emotion.Calm));

        MoodAggregate.HourlyObservations(hourly)
            .Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Hour = 14, Mood = Emotion.Calm });
    }
}
