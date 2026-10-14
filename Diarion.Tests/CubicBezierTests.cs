using System;
using Diarion.Helpers;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

public class CubicBezierTests
{
    [Fact]
    public void Solve_ClampsOutsideUnitRange()
    {
        var curve = CubicBezier.EaseOut;

        curve.Solve(-0.5).Should().Be(0);
        curve.Solve(0).Should().Be(0);
        curve.Solve(1).Should().Be(1);
        curve.Solve(1.5).Should().Be(1);
    }

    [Fact]
    public void Solve_LinearControlPoints_IsIdentity()
    {
        var linear = new CubicBezier(0, 0, 1, 1);

        for (var x = 0.0; x <= 1.0; x += 0.1)
            linear.Solve(x).Should().BeApproximately(x, 1e-6);
    }

    // Reference values computed independently with a 200-step bisection on the same polynomials.
    [Theory]
    [InlineData(0.1, 0.398124)]
    [InlineData(0.25, 0.775382)]
    [InlineData(0.5, 0.965983)]
    [InlineData(0.75, 0.997362)]
    [InlineData(0.9, 0.999865)]
    public void EaseOut_MatchesReference(double x, double expected)
    {
        CubicBezier.EaseOut.Solve(x).Should().BeApproximately(expected, 1e-4);
    }

    [Theory]
    [InlineData(0.1, 0.006445)]
    [InlineData(0.25, 0.052889)]
    [InlineData(0.5, 0.595971)]
    [InlineData(0.75, 0.956282)]
    [InlineData(0.9, 0.994468)]
    public void EaseInOut_MatchesReference(double x, double expected)
    {
        CubicBezier.EaseInOut.Solve(x).Should().BeApproximately(expected, 1e-4);
    }

    [Theory]
    [InlineData(0.1, 0.269821)]
    [InlineData(0.25, 0.779131)]
    [InlineData(0.5, 0.954809)]
    [InlineData(0.75, 0.992349)]
    [InlineData(0.9, 0.998969)]
    public void Sheet_MatchesReference(double x, double expected)
    {
        CubicBezier.Sheet.Solve(x).Should().BeApproximately(expected, 1e-4);
    }

    [Fact]
    public void EaseOut_IsMonotonicAndFrontLoaded()
    {
        var curve = CubicBezier.EaseOut;
        var previous = 0.0;

        for (var i = 1; i <= 100; i++)
        {
            var value = curve.Solve(i / 100.0);
            value.Should().BeGreaterThanOrEqualTo(previous);
            previous = value;
        }

        // An ease-out spends its motion early: past halfway it is already almost settled.
        curve.Solve(0.5).Should().BeGreaterThan(0.9);
    }

    [Theory]
    [InlineData(-0.1, 0, 0.5, 1)]
    [InlineData(0.5, 0, 1.1, 1)]
    public void Constructor_RejectsControlXOutsideUnitRange(double x1, double y1, double x2, double y2)
    {
        var act = () => new CubicBezier(x1, y1, x2, y2);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_AllowsControlYOutsideUnitRange_ForOvershoot()
    {
        var overshoot = new CubicBezier(0.175, 0.885, 0.32, 1.275);

        overshoot.Solve(0.7).Should().BeGreaterThan(1);
        overshoot.Solve(1).Should().Be(1);
    }
}
