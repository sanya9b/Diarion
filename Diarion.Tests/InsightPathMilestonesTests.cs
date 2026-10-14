using Diarion.Services;
using Diarion.Services.InsightPath;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class InsightPathMilestonesTests
{
    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 3)]
    [InlineData(2, 1, 3)]
    [InlineData(3, 3, 7)]
    [InlineData(6, 3, 7)]
    [InlineData(7, 7, 14)]
    [InlineData(13, 7, 14)]
    [InlineData(14, 14, null)]
    [InlineData(20, 14, null)]
    public void Reached_And_Next_BracketTheCount(int pairedDays, int reached, int? next)
    {
        InsightPathMilestones.Reached(pairedDays).Should().Be(reached);
        InsightPathMilestones.Next(pairedDays).Should().Be(next);
    }

    [Fact]
    public void All_IsAscending_AndEndsWhereTheEngineStartsReporting()
    {
        InsightPathMilestones.All.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();

        // The last milestone is the one that shows a hypothesis, which exists only once the engine
        // has its minimum sample. Held together by this test rather than a reference, so the table
        // stays free of the engine.
        InsightPathMilestones.Last.Should().Be(CorrelationService.MinSampleSize);
    }

    [Fact]
    public void Reached_WithANegativeCount_IsBeforeTheFirstMilestone()
    {
        InsightPathMilestones.Reached(-1).Should().Be(0);
        InsightPathMilestones.Next(-1).Should().Be(1);
    }
}
