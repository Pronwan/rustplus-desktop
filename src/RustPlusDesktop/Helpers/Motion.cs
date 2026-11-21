using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace RustPlusDesk.Helpers;

/// <summary>
/// The app's motion tokens: three durations and the Fluent easing curves (Microsoft Learn,
/// "Timing and easing"). Code-driven animations take their timing from here so the app moves
/// with one rhythm; XAML uses the matching MotionFaster/Fast/Normal resources.
/// </summary>
public static class Motion
{
    /// <summary>Fades and small state changes.</summary>
    public static readonly TimeSpan Faster = TimeSpan.FromMilliseconds(83);

    /// <summary>Exits and most control transitions.</summary>
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(167);

    /// <summary>Entrances and larger moves.</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(250);

    /// <summary>Off when the user turned off "Animation effects" in Windows.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    /// <summary>The duration to use: zero when animation is turned off, so it completes at once.</summary>
    public static Duration For(TimeSpan duration) => new(Enabled ? duration : TimeSpan.Zero);

    /// <summary>Fast in, slow to rest: for anything entering or settling.</summary>
    public static IEasingFunction Enter { get; } = Freeze(new CubicBezierEase(0, 0, 0, 1));

    /// <summary>Slow out, fast away: for anything leaving.</summary>
    public static IEasingFunction Exit { get; } = Freeze(new CubicBezierEase(1, 0, 1, 1));

    /// <summary>For elements moving between two resting places.</summary>
    public static IEasingFunction PointToPoint { get; } = Freeze(new CubicBezierEase(0.55, 0.55, 0, 1));

    private static IEasingFunction Freeze(CubicBezierEase ease)
    {
        ease.Freeze();
        return ease;
    }
}

/// <summary>
/// CSS-style cubic-bezier easing, so animations can use the exact Fluent curves. Usable from XAML:
/// <c>&lt;helpers:CubicBezierEase X1="0" Y1="0" X2="0" Y2="1" /&gt;</c>.
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(nameof(X1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(nameof(Y1), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(nameof(X2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(nameof(Y2), typeof(double), typeof(CubicBezierEase), new PropertyMetadata(1.0));

    public CubicBezierEase()
    {
        // The curve is used as given; EaseOut (the base default) would mirror it.
        EasingMode = EasingMode.EaseIn;
    }

    public CubicBezierEase(double x1, double y1, double x2, double y2) : this()
    {
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
    }

    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    protected override double EaseInCore(double normalizedTime)
    {
        double x = Math.Clamp(normalizedTime, 0, 1);
        if (x is 0 or 1) return x;
        return Sample(SolveCurveT(x), Y1, Y2);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase();

    private static double Sample(double t, double p1, double p2) =>
        ((1 - 3 * p2 + 3 * p1) * t + (3 * p2 - 6 * p1)) * t * t + 3 * p1 * t;

    private static double Slope(double t, double p1, double p2) =>
        3 * (1 - 3 * p2 + 3 * p1) * t * t + 2 * (3 * p2 - 6 * p1) * t + 3 * p1;

    // Finds the curve parameter for a given progress: Newton's method, with bisection as a fallback.
    private double SolveCurveT(double x)
    {
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double error = Sample(t, X1, X2) - x;
            if (Math.Abs(error) < 1e-6) return t;
            double slope = Slope(t, X1, X2);
            if (Math.Abs(slope) < 1e-6) break;
            t -= error / slope;
        }

        double low = 0, high = 1;
        t = x;
        for (int i = 0; i < 32; i++)
        {
            double value = Sample(t, X1, X2);
            if (Math.Abs(value - x) < 1e-6) break;
            if (value < x) low = t; else high = t;
            t = (low + high) / 2;
        }
        return t;
    }
}
