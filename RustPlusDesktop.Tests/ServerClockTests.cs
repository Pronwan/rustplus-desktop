using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ServerClockTests
{
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
