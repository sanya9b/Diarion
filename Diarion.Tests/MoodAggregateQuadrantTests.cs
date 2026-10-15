using System.Collections.Generic;
using System.Linq;
using Diarion.Models;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>The second axis and the coarser grain <see cref="MoodAggregate"/> grew with the circumplex picker.</summary>
public class MoodAggregateQuadrantTests
{
    private static List<HourMood> Hours(params (int Hour, Emotion Mood)[] entries) =>
        entries.Select(e => new HourMood { Hour = e.Hour, Mood = e.Mood }).ToList();

    [Fact]
    public void Energy_AveragesArousalAcrossTheDaysObservations()
    {
        // Angry +2, Tired -2, Happy +1.
        var hourly = Hours((9, Emotion.Angry), (13, Emotion.Tired), (20, Emotion.Happy));

        MoodAggregate.Energy(Emotion.None, hourly).Should().BeApproximately(1.0 / 3, 1e-9);
    }

    [Fact]
    public void Energy_FallsBackToTheScalar_AndIsZeroWhenNothingWasRecorded()
    {
        MoodAggregate.Energy(Emotion.Relaxed, null).Should().Be(-2);
        MoodAggregate.Energy(Emotion.None, null).Should().Be(0);
        MoodAggregate.Energy(Emotion.None, Hours((9, Emotion.None))).Should().Be(0);
    }

    [Fact]
    public void DominantQuadrant_CountsAtQuadrantGrain_WhereDominantCountsWords()
    {
        // Three different unpleasant-energetic states against two identical happy ones: the single
        // most repeated word is Happy, the day is not.
        var hourly = Hours(
            (9, Emotion.Anxious), (11, Emotion.Stressed), (15, Emotion.Overwhelmed),
            (19, Emotion.Happy), (21, Emotion.Happy));

        MoodAggregate.Dominant(Emotion.None, hourly).Should().Be(Emotion.Happy);
        MoodAggregate.DominantQuadrant(Emotion.None, hourly).Should().Be(EmotionQuadrant.UnpleasantEnergetic);
    }

    [Fact]
    public void DominantQuadrant_BreaksTiesOnEnumOrder()
    {
        var hourly = Hours((9, Emotion.Sad), (10, Emotion.Calm));

        MoodAggregate.DominantQuadrant(Emotion.None, hourly).Should().Be(EmotionQuadrant.PleasantCalm);
    }

    [Fact]
    public void DominantQuadrant_UsesTheScalar_WhenNoHourIsLogged()
    {
        MoodAggregate.DominantQuadrant(Emotion.Neutral, new List<HourMood>()).Should().Be(EmotionQuadrant.Neutral);
        MoodAggregate.DominantQuadrant(Emotion.Lonely, null).Should().Be(EmotionQuadrant.UnpleasantCalm);
    }

    [Fact]
    public void DominantQuadrant_IsNone_WhenNothingWasRecorded()
    {
        MoodAggregate.DominantQuadrant(Emotion.None, null).Should().Be(EmotionQuadrant.None);
        MoodAggregate.DominantQuadrant(Emotion.None, Hours((9, Emotion.None))).Should().Be(EmotionQuadrant.None);
    }
}
