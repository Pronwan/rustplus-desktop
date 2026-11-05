using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class ServerClockTests
{
    [TestMethod]
    public void ReportedCycleSplitsSeventyMinutesOfDayAndTenOfNight()
    {
        // Live Facepunch Small reading: 80-minute cycle, scale 1.
        var clock = new ServerClock();
        clock.Observe(9.23, DateTime.UtcNow, 7.5836, 19.8963, 80, 1);
        Assert.AreEqual(70, (clock.Sunset - clock.Sunrise) / clock.DaySpeed, 1e-9);
        Assert.AreEqual(10, (24 - clock.Sunset + clock.Sunrise) / clock.NightSpeed, 1e-9);
        // 09:14 is about 64 measured minutes from night; the even split says 60.6, not 72.
        Assert.AreEqual(60.64, (clock.Sunset - 9.23) / clock.DaySpeed, 0.01);
    }

    // Polls a server every 15 s whose night (19:00 to 07:00) really lasts nightMinutes.
    private static ServerClock PollThroughNight(double nightMinutes)
    {
        var start = DateTime.UtcNow;
        double dayPace = 12.0 / 70, nightPace = 12.0 / nightMinutes, dusk = 0.5 / dayPace;
        var clock = new ServerClock();
        for (double t = 0; t <= dusk + nightMinutes + 1; t += 0.25)
        {
            double h = t <= dusk ? 18.5 + t * dayPace
                : t <= dusk + nightMinutes ? 19 + (t - dusk) * nightPace
                : 7 + (t - dusk - nightMinutes) * dayPace;
            clock.Observe(h % 24, start.AddMinutes(t), 7, 19, 80, 1);
        }
        return clock;
    }

    [TestMethod]
    public void ModdedShortNightIsMeasuredAndReplacesTheTenMinuteShare()
    {
        var clock = PollThroughNight(3);
        Assert.AreEqual(3, clock.MeasuredNightMinutes!.Value, 0.25);
        Assert.AreEqual(clock.MeasuredNightMinutes.Value, 12 / clock.NightSpeed, 1e-9);
        Assert.AreEqual(12.0 / 70, clock.DaySpeed, 1e-10, "the day was not seen whole, so it keeps its share");

        clock = PollThroughNight(10);
        Assert.AreEqual(10, clock.MeasuredNightMinutes!.Value, 0.25);
        Assert.AreEqual(1.2, clock.NightSpeed, 1e-10, "a vanilla night stays on the 10-minute share");
    }

    [TestMethod]
    public void ReportedCycleOverridesObservationsAndUpdatesImmediately()
    {
        var now = DateTime.UtcNow;
        var clock = new ServerClock();
        clock.Observe(14, now);
        clock.Observe(14.16, now.AddMinutes(1));
        Assert.AreEqual(0.16, clock.DaySpeed, 1e-10);
        clock.Observe(14.5, now.AddSeconds(61), 7, 19, 80, 1);
        Assert.AreEqual(12.0 / 70, clock.DaySpeed, 1e-10);
        Assert.AreEqual(12.0 / 10, clock.NightSpeed, 1e-10);
        clock.Observe(14.65, now.AddSeconds(121), 7, 19, 80, 1);
        Assert.AreEqual(12.0 / 70, clock.DaySpeed, 1e-10, "observations do not override the reported cycle");
        clock.Observe(14.5, now.AddSeconds(122), 7, 19, 80, 2);
        Assert.AreEqual(24.0 / 70, clock.DaySpeed, 1e-10);
        clock.Observe(14.5, now.AddSeconds(123), 7, 19, 40, 1);
        Assert.AreEqual(24.0 / 70, clock.DaySpeed, 1e-10);
        clock.Observe(14.5, now.AddSeconds(124), 6, 20, 80, 1);
        Assert.AreEqual(14.0 / 70, clock.DaySpeed, 1e-10, "follows the server's sunrise and sunset");
        Assert.AreEqual(10.0 / 10, clock.NightSpeed, 1e-10);
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
        Assert.AreEqual(12.0 / 70, clock.DaySpeed, 1e-10);
        clock = new ServerClock();
        clock.Observe(14.5, now, 7, 19, 80, double.NaN);
        Assert.AreEqual(0.24, clock.DaySpeed, 1e-10);
        clock.Observe(14.5, now, 7, 19, 80);
        Assert.IsFalse(clock.IsPaused);
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

        clock.Observe(18.5, DateTime.UtcNow, 7, 19, 80, 1);
        // 0.1 game hours of day take 0.5833 minutes; the rest of the minute runs at night speed.
        Assert.AreEqual(19 + (1 - 0.1 / (12.0 / 70)) * 1.2, clock.Advance(18.9, 1), 1e-10);
    }
}
