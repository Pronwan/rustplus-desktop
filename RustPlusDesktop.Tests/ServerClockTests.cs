using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ServerClockTests
{
    [TestMethod]
    public void ReportedClockRateOverridesObservationsAndUpdatesImmediately()
    {
        var now = DateTime.UtcNow;
        var clock = new ServerClock();
        clock.Observe(14, now);
        clock.Observe(14.16, now.AddMinutes(1));
        clock.Observe(14.5, now.AddSeconds(61), 7, 19, 80, 1);
        Assert.AreEqual(0.3, clock.DaySpeed, 1e-10);
        Assert.AreEqual(0.3, clock.NightSpeed, 1e-10);
        Assert.AreEqual(15, (clock.Sunset - 14.5) / clock.DaySpeed, 1e-10);
        Assert.AreEqual(25, (24 - 23.5 + clock.Sunrise) / clock.NightSpeed, 1e-10);
        clock.Observe(14.5, now.AddSeconds(62), 7, 19, 80, 2);
        Assert.AreEqual(0.6, clock.DaySpeed, 1e-10);
        clock.Observe(14.5, now.AddSeconds(63), 7, 19, 40, 1);
        Assert.AreEqual(0.6, clock.DaySpeed, 1e-10);
        Assert.AreEqual(19.5, clock.Advance(18.9, 1), 1e-10);
    }

    [TestMethod]
    public void FrozenTimeStopsExtrapolationAndInvalidMetadataFallsBack()
    {
        var now = DateTime.UtcNow;
        var clock = new ServerClock();
        clock.Observe(14.5, now, 7, 19, 80, 0);
        Assert.IsTrue(clock.IsPaused);
        Assert.AreEqual(14.5, clock.Advance(14.5, 1), 1e-10);
        clock.Observe(14.5, now.AddSeconds(1), 7, 19, 0, 1);
        Assert.IsFalse(clock.IsPaused);
        Assert.AreEqual(0.24, clock.DaySpeed, 1e-10);
        clock.Observe(14.5, now.AddSeconds(2), 7, 19, 80, 1);
        Assert.AreEqual(0.3, clock.DaySpeed, 1e-10);
        clock = new ServerClock();
        clock.Observe(14.5, now, 7, 19, 80, double.NaN);
        Assert.AreEqual(0.24, clock.DaySpeed, 1e-10);
        clock.Observe(14.5, now, 7, 19, 80);
        Assert.IsFalse(clock.IsPaused);
    }

    [TestMethod]
    public void UnreportedCurveIsCalibratedAndPreservedAcrossSpeedChangesAndPause()
    {
        var now = DateTime.UtcNow;
        var clock = new ServerClock();
        clock.Observe(10.6, now, 7.26, 19.76, 80, 1);
        clock.Observe(10.75, now.AddMinutes(1), 7.26, 19.76, 80, 1);
        Assert.AreEqual(0.15, clock.DaySpeed, 1e-10);
        Assert.AreEqual(0.3, clock.NightSpeed, 1e-10);
        clock.Observe(10.75, now.AddSeconds(61), 7.26, 19.76, 80, 2);
        Assert.AreEqual(0.3, clock.DaySpeed, 1e-10);
        Assert.AreEqual(0.6, clock.NightSpeed, 1e-10);
        clock.Observe(10.75, now.AddSeconds(62), 7.26, 19.76, 80, 0);
        Assert.AreEqual(10.75, clock.Advance(10.75, 1), 1e-10);
        clock.Observe(10.75, now.AddSeconds(63), 7.26, 19.76, 80, 2);
        Assert.AreEqual(0.3, clock.DaySpeed, 1e-10);
    }

    [TestMethod]
    public void LearnsStableRawSamplesAndRejectsPhaseChangesAndStaleGaps()
    {
        var start = DateTime.UtcNow;
        var clock = new ServerClock();
        clock.Observe(11, start);
        for (int seconds = 3; seconds <= 60; seconds += 3)
            clock.Observe(11 + 0.24 * seconds / 60, start.AddSeconds(seconds));
        Assert.AreEqual(0.24, clock.DaySpeed, 1e-10);

        clock = new ServerClock();
        clock.Observe(11, start);
        clock.Observe(11.16, start.AddMinutes(1));
        Assert.AreEqual(0.16, clock.DaySpeed, 1e-10);
        clock.Observe(19.9, start.AddMinutes(2));
        clock.Observe(20.2, start.AddSeconds(150));
        Assert.AreEqual(1.2, clock.NightSpeed, 1e-10);
        clock.Observe(21, start.AddMinutes(10));
        Assert.AreEqual(1.2, clock.NightSpeed, 1e-10);

        clock = new ServerClock();
        clock.Observe(23.8, start);
        clock.Observe(0.4, start.AddSeconds(30));
        Assert.AreEqual(1.2, clock.NightSpeed, 1e-10);
    }

    [TestMethod]
    public void ExtrapolationChangesSpeedAtDayNightBoundary()
    {
        var clock = new ServerClock();
        Assert.AreEqual(20.7, clock.Advance(19.9, 1), 1e-10);
        Assert.AreEqual(8.22, clock.Advance(7.9, 1), 1e-10);
        Assert.AreEqual(0.4, clock.Advance(23.8, 0.5), 1e-10);
        clock.Observe(18.5, DateTime.UtcNow, 7, 19);
        Assert.IsTrue(clock.IsDay(18.5));
        Assert.IsFalse(clock.IsDay(19));
        Assert.AreEqual(19.7, clock.Advance(18.9, 1), 1e-10);
    }
}
