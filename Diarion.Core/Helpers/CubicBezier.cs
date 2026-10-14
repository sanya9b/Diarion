using System;

namespace Diarion.Helpers;

/// <summary>
/// Cubic Bézier easing curve from (0,0) to (1,1) with two control points — the same parametrisation
/// as CSS <c>cubic-bezier(x1, y1, x2, y2)</c>. Lets the app reuse curves published for the web
/// (easing.dev, Material, iOS) instead of approximating them with the built-in <c>CubicOut</c> family.
/// </summary>
public readonly struct CubicBezier
{
    /// <summary>Strong ease-out for anything entering or exiting: cubic-bezier(0.23, 1, 0.32, 1).</summary>
    public static readonly CubicBezier EaseOut = new(0.23, 1, 0.32, 1);

    /// <summary>Strong ease-in-out for movement that stays on screen: cubic-bezier(0.77, 0, 0.175, 1).</summary>
    public static readonly CubicBezier EaseInOut = new(0.77, 0, 0.175, 1);

    /// <summary>iOS-like sheet/drawer curve: cubic-bezier(0.32, 0.72, 0, 1).</summary>
    public static readonly CubicBezier Sheet = new(0.32, 0.72, 0, 1);

    private const double Epsilon = 1e-7;

    private readonly double _ax, _bx, _cx;
    private readonly double _ay, _by, _cy;

    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }

    /// <param name="x1">First control point X, must be within [0, 1] so the curve stays a function of time.</param>
    /// <param name="y1">First control point Y; values outside [0, 1] produce overshoot.</param>
    /// <param name="x2">Second control point X, must be within [0, 1].</param>
    /// <param name="y2">Second control point Y.</param>
    public CubicBezier(double x1, double y1, double x2, double y2)
    {
        if (x1 < 0 || x1 > 1)
            throw new ArgumentOutOfRangeException(nameof(x1), x1, "Control point X must be within [0, 1].");
        if (x2 < 0 || x2 > 1)
            throw new ArgumentOutOfRangeException(nameof(x2), x2, "Control point X must be within [0, 1].");

        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;

        // Polynomial coefficients, with P0 = (0,0) and P3 = (1,1) implied.
        _cx = 3.0 * x1;
        _bx = 3.0 * (x2 - x1) - _cx;
        _ax = 1.0 - _cx - _bx;

        _cy = 3.0 * y1;
        _by = 3.0 * (y2 - y1) - _cy;
        _ay = 1.0 - _cy - _by;
    }

    /// <summary>
    /// Progress (Y) for normalised time <paramref name="x"/> in [0, 1]. Inputs outside the range clamp,
    /// which is what <c>Microsoft.Maui.Easing</c> expects from an easing function.
    /// </summary>
    public double Solve(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        return SampleY(SolveT(x));
    }

    private double SampleX(double t) => ((_ax * t + _bx) * t + _cx) * t;

    private double SampleY(double t) => ((_ay * t + _by) * t + _cy) * t;

    private double SampleDerivativeX(double t) => (3.0 * _ax * t + 2.0 * _bx) * t + _cx;

    /// <summary>Find the curve parameter t whose X equals <paramref name="x"/>: Newton first, bisection as the safety net.</summary>
    private double SolveT(double x)
    {
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = SampleX(t) - x;
            if (Math.Abs(error) < Epsilon) return t;

            var derivative = SampleDerivativeX(t);
            if (Math.Abs(derivative) < 1e-6) break;

            t -= error / derivative;
        }

        double low = 0, high = 1;
        t = x;
        while (high - low > Epsilon)
        {
            var sampled = SampleX(t);
            if (Math.Abs(sampled - x) < Epsilon) return t;

            if (x > sampled) low = t;
            else high = t;

            t = low + (high - low) / 2;
        }

        return t;
    }
}
