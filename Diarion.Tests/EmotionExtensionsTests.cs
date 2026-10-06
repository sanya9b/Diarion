using Diarion.Models;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class EmotionExtensionsTests
{
    [Theory]
    [InlineData(Emotion.Happy, 2)]
    [InlineData(Emotion.Calm, 1)]
    [InlineData(Emotion.Anxious, -1)]
    [InlineData(Emotion.Sad, -2)]
    [InlineData(Emotion.Angry, -2)]
    [InlineData(Emotion.None, 0)]
    public void ToValence_MapsEachEmotion(Emotion emotion, int expected)
    {
        emotion.ToValence().Should().Be(expected);
    }

    [Theory]
    [InlineData(Emotion.Happy, "#C26D53")]
    [InlineData(Emotion.Calm, "#8FA083")]
    [InlineData(Emotion.Anxious, "#C9985A")]
    [InlineData(Emotion.Sad, "#929FA7")]
    [InlineData(Emotion.Angry, "#A87C8E")]
    public void ToColorHex_MapsEachEmotionToBrandColor(Emotion emotion, string expected)
    {
        emotion.ToColorHex().Should().Be(expected);
    }
}
