using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Helpers;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MotionTests
{
    [TestMethod]
    [DataRow(0.0, 0.0, 0.0, 1.0)]
    [DataRow(1.0, 0.0, 1.0, 1.0)]
    [DataRow(0.55, 0.55, 0.0, 1.0)]
    public void CubicBezierEase_StartsAtZeroEndsAtOneAndNeverGoesBackwards(double x1, double y1, double x2, double y2)
    {
        var ease = new CubicBezierEase(x1, y1, x2, y2);
        Assert.AreEqual(0.0, ease.Ease(0), 1e-9);
        Assert.AreEqual(1.0, ease.Ease(1), 1e-9);
        double previous = 0;
        for (int i = 1; i <= 100; i++)
        {
            double value = ease.Ease(i / 100.0);
            Assert.IsTrue(value >= previous - 1e-6, $"Eased value went backwards at {i}%.");
            previous = value;
        }
    }

    [TestMethod]
    public void EnterAndExitCurves_HaveTheFluentShape()
    {
        // Enter is fast early and settles; exit starts slowly and accelerates away.
        Assert.IsTrue(Motion.Enter.Ease(0.25) > 0.6, "Enter should cover most of the distance early.");
        Assert.IsTrue(Motion.Exit.Ease(0.25) < 0.1, "Exit should start slowly.");
        Assert.AreEqual(0.5, new CubicBezierEase(0.25, 0.25, 0.75, 0.75).Ease(0.5), 1e-4, "A straight curve is linear.");
    }

    [TestMethod]
    public void Durations_MatchTheWindowsMotionValues()
    {
        Assert.AreEqual(83, Motion.Faster.TotalMilliseconds);
        Assert.AreEqual(167, Motion.Fast.TotalMilliseconds);
        Assert.AreEqual(250, Motion.Normal.TotalMilliseconds);
    }
}
