using System;
using System.Collections.Generic;
using System.Linq;

namespace Diarion.Services;

/// <summary>
/// The maths behind the insight cards, kept apart from the service that assembles the data so it can
/// be checked against known values instead of against whatever the database happens to hold.
/// </summary>
public static class CorrelationStatistics
{
    /// <summary>Pearson's r, or 0 when a series has no variance and r is undefined.</summary>
    public static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = x.Count;
        if (n != y.Count || n < 2)
        {
            return 0;
        }

        double meanX = x.Average(), meanY = y.Average();
        double cov = 0, varX = 0, varY = 0;

        for (var i = 0; i < n; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            cov += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        return varX <= 0 || varY <= 0 ? 0 : cov / Math.Sqrt(varX * varY);
    }

    /// <summary>
    /// Two-sided p for r over n pairs, via the Fisher z-transform. Returns 1 below four pairs, where
    /// the transform has no usable standard error — no evidence rather than weak evidence.
    /// </summary>
    public static double PValue(double r, int n)
    {
        if (n < 4)
        {
            return 1.0;
        }

        var z = Math.Atanh(Math.Clamp(r, -0.999999, 0.999999));
        var statistic = Math.Abs(z) * Math.Sqrt(n - 3);
        return 2.0 * (1.0 - StandardNormalCdf(statistic));
    }

    /// <summary>
    /// Benjamini-Hochberg adjusted p-values, in the order the inputs were given.
    /// <para>
    /// This is what makes a wide factor set honest. Testing twenty factors at p &lt; 0.05 produces a
    /// false positive on most days by construction, and a diary that invents a new "discovery" every
    /// morning is worse than one that finds nothing — it is the specific complaint levelled at apps
    /// that auto-mine correlations. Controlling the false discovery rate keeps the cards believable
    /// as the factor list grows.
    /// </para>
    /// <para>
    /// Adjusted values are enforced monotonic from the largest p downwards, so a factor can never be
    /// reported as stronger evidence than one with a smaller raw p.
    /// </para>
    /// </summary>
    public static double[] BenjaminiHochberg(IReadOnlyList<double> pValues)
    {
        var m = pValues.Count;
        var adjusted = new double[m];
        if (m == 0)
        {
            return adjusted;
        }

        var order = Enumerable.Range(0, m).OrderBy(i => pValues[i]).ToArray();

        var runningMinimum = 1.0;
        for (var rank = m; rank >= 1; rank--)
        {
            var index = order[rank - 1];
            var scaled = pValues[index] * m / rank;
            runningMinimum = Math.Min(runningMinimum, scaled);
            adjusted[index] = Math.Clamp(runningMinimum, 0.0, 1.0);
        }

        return adjusted;
    }

    /// <summary>
    /// Confidence as one to five dots, read off the adjusted p so the display tightens automatically
    /// when more factors are being tested.
    /// </summary>
    public static int ConfidenceDots(double adjustedP) => adjustedP switch
    {
        < 0.001 => 5,
        < 0.01 => 4,
        < 0.05 => 3,
        < 0.10 => 2,
        _ => 1
    };

    /// <summary>
    /// Abramowitz &amp; Stegun 7.1.26 for erf, good to about 1.5e-7 — far tighter than the five buckets
    /// the result is eventually squeezed into.
    /// </summary>
    private static double StandardNormalCdf(double x) => 0.5 * (1.0 + Erf(x / Math.Sqrt(2.0)));

    private static double Erf(double x)
    {
        var sign = Math.Sign(x);
        x = Math.Abs(x);

        const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741;
        const double a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;

        var t = 1.0 / (1.0 + p * x);
        var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return sign * y;
    }
}
