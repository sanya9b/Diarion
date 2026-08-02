using System;
using System.Linq;
using Diarion.Services;
using FluentAssertions;
using Xunit;

namespace Diarion.Tests;

/// <summary>
/// The maths on its own, against values that can be worked out by hand. The service that uses it can
/// only be tested against whatever data a test happens to build, which proves the plumbing but not
/// the arithmetic.
/// </summary>
public class CorrelationStatisticsTests
{
    [Fact]
    public void Pearson_finds_a_perfect_line()
    {
        var x = new[] { 1.0, 2, 3, 4, 5 };
        var y = new[] { 2.0, 4, 6, 8, 10 };

        CorrelationStatistics.Pearson(x, y).Should().BeApproximately(1.0, 1e-9);
        CorrelationStatistics.Pearson(x, y.Select(v => -v).ToArray()).Should().BeApproximately(-1.0, 1e-9);
    }

    [Fact]
    public void Pearson_returns_zero_when_a_series_never_varies()
    {
        // r is undefined here rather than zero, but a flat series carries no information either way
        // and the alternative is NaN leaking into the display.
        var flat = new[] { 3.0, 3, 3, 3 };
        var varying = new[] { 1.0, 2, 3, 4 };

        CorrelationStatistics.Pearson(flat, varying).Should().Be(0);
    }

    [Fact]
    public void PValue_matches_the_textbook_figure()
    {
        // r = 0.5 over 20 pairs: Fisher z = 0.5493, × sqrt(17) = 2.2649, two-sided p ≈ 0.0235.
        CorrelationStatistics.PValue(0.5, 20).Should().BeApproximately(0.0235, 0.001);
    }

    [Fact]
    public void PValue_reports_no_evidence_below_four_pairs()
    {
        CorrelationStatistics.PValue(0.99, 3).Should().Be(1.0);
    }

    [Fact]
    public void PValue_falls_as_the_sample_grows()
    {
        var small = CorrelationStatistics.PValue(0.4, 15);
        var large = CorrelationStatistics.PValue(0.4, 150);

        large.Should().BeLessThan(small, "the same effect is better evidenced by more days");
    }

    [Fact]
    public void BenjaminiHochberg_leaves_a_lone_test_untouched()
    {
        CorrelationStatistics.BenjaminiHochberg(new[] { 0.03 })[0].Should().BeApproximately(0.03, 1e-12);
    }

    [Fact]
    public void BenjaminiHochberg_matches_a_hand_worked_example()
    {
        // Five evenly spaced p-values all scale to exactly 0.05: p(k) × m / k is 0.05 for every k.
        var adjusted = CorrelationStatistics.BenjaminiHochberg(new[] { 0.01, 0.02, 0.03, 0.04, 0.05 });

        adjusted.Should().AllSatisfy(p => p.Should().BeApproximately(0.05, 1e-12));
    }

    [Fact]
    public void BenjaminiHochberg_keeps_the_input_order()
    {
        var adjusted = CorrelationStatistics.BenjaminiHochberg(new[] { 0.5, 0.001 });

        adjusted[0].Should().BeApproximately(0.5, 1e-12);
        adjusted[1].Should().BeApproximately(0.002, 1e-12);
    }

    [Fact]
    public void BenjaminiHochberg_never_reports_stronger_evidence_than_the_raw_value()
    {
        var raw = new[] { 0.001, 0.008, 0.02, 0.2, 0.7, 0.9 };

        var adjusted = CorrelationStatistics.BenjaminiHochberg(raw);

        for (var i = 0; i < raw.Length; i++)
        {
            adjusted[i].Should().BeGreaterThanOrEqualTo(raw[i] - 1e-12);
        }
    }

    [Fact]
    public void BenjaminiHochberg_stays_monotonic_with_the_ranking()
    {
        // A factor with a smaller raw p must never end up looking weaker than one behind it.
        var raw = new[] { 0.001, 0.04, 0.041, 0.9 };

        var adjusted = CorrelationStatistics.BenjaminiHochberg(raw);

        adjusted.Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_marginal_finding_stops_being_reportable_once_many_factors_are_tested()
    {
        // This is the whole reason the correction exists. On its own, p = 0.04 clears the bar the UI
        // uses to show a card. Tested alongside seven other factors it does not, and without this the
        // app would announce a discovery on most days purely from the number of things it measures.
        const double marginal = 0.04;

        CorrelationStatistics.ConfidenceDots(marginal).Should().BeGreaterThanOrEqualTo(3);

        var withSevenOthers = CorrelationStatistics.BenjaminiHochberg(
            new[] { marginal, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9 });

        withSevenOthers[0].Should().BeApproximately(0.32, 1e-12);
        CorrelationStatistics.ConfidenceDots(withSevenOthers[0]).Should().BeLessThan(3);
    }

    [Fact]
    public void A_strong_finding_survives_the_correction()
    {
        // The other half of the bargain: the correction must not bury genuine signal.
        var adjusted = CorrelationStatistics.BenjaminiHochberg(
            new[] { 0.00001, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9, 0.9 });

        CorrelationStatistics.ConfidenceDots(adjusted[0]).Should().Be(5);
    }

    [Fact]
    public void BenjaminiHochberg_handles_an_empty_set()
    {
        CorrelationStatistics.BenjaminiHochberg(Array.Empty<double>()).Should().BeEmpty();
    }
}
