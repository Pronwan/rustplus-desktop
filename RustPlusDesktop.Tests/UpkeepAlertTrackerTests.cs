using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class UpkeepAlertTrackerTests
{
    private const int Hour = 3600;
    private const int Minute = 60;

    [TestMethod]
    public void NoAlertWhileUpkeepIsHealthy()
    {
        var tracker = new UpkeepAlertTracker();

        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 10 * Hour));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 2 * Hour + 1));
    }

    [TestMethod]
    public void WarnsOnceAtEachThreshold()
    {
        var tracker = new UpkeepAlertTracker();

        Assert.AreEqual(2 * Hour, tracker.Evaluate("tc", 2 * Hour));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 90 * Minute));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 31 * Minute));
        Assert.AreEqual(30 * Minute, tracker.Evaluate("tc", 30 * Minute));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 10 * Minute));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 0));
    }

    [TestMethod]
    public void SkippingStraightToTheLowestThresholdGivesOneMessage()
    {
        var tracker = new UpkeepAlertTracker();

        Assert.AreEqual(30 * Minute, tracker.Evaluate("tc", 10 * Minute));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 9 * Minute));
    }

    [TestMethod]
    public void UnknownUpkeepDoesNothingAndKeepsState()
    {
        var tracker = new UpkeepAlertTracker();
        tracker.Evaluate("tc", 20 * Minute);

        Assert.AreEqual((int?)null, tracker.Evaluate("tc", null));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 19 * Minute));
    }

    [TestMethod]
    public void RefillingRearmsTheWarnings()
    {
        var tracker = new UpkeepAlertTracker();
        Assert.AreEqual(30 * Minute, tracker.Evaluate("tc", 20 * Minute));

        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 24 * Hour)); // refilled
        Assert.AreEqual(2 * Hour, tracker.Evaluate("tc", 2 * Hour));
        Assert.AreEqual(30 * Minute, tracker.Evaluate("tc", 29 * Minute));
    }

    [TestMethod]
    public void WobbleAroundAThresholdDoesNotRepeatTheWarning()
    {
        var tracker = new UpkeepAlertTracker();
        Assert.AreEqual(30 * Minute, tracker.Evaluate("tc", 29 * Minute));

        // A fresh snapshot reads a little higher, then it decays again.
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 31 * Minute));
        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 29 * Minute));
    }

    [TestMethod]
    public void PartialRefillAboveTheLowThresholdWarnsAgainWhenItDropsBack()
    {
        var tracker = new UpkeepAlertTracker();
        Assert.AreEqual(30 * Minute, tracker.Evaluate("tc", 10 * Minute));

        Assert.AreEqual((int?)null, tracker.Evaluate("tc", 90 * Minute)); // topped up to 1.5h
        Assert.AreEqual(30 * Minute, tracker.Evaluate("tc", 29 * Minute));
    }

    [TestMethod]
    public void CupboardsAreTrackedIndependently()
    {
        var tracker = new UpkeepAlertTracker();

        Assert.AreEqual(2 * Hour, tracker.Evaluate("a", 100 * Minute));
        Assert.AreEqual(2 * Hour, tracker.Evaluate("b", 100 * Minute));
        Assert.AreEqual((int?)null, tracker.Evaluate("a", 99 * Minute));
    }

    [TestMethod]
    public void CustomThresholdsAreSortedAndValidated()
    {
        var tracker = new UpkeepAlertTracker(new[] { 600, 3600 });

        Assert.AreEqual(3600, tracker.Evaluate("tc", 3000));
        Assert.AreEqual(600, tracker.Evaluate("tc", 500));
        Assert.IsFalse(CreatesWithoutThresholds());
    }

    private static bool CreatesWithoutThresholds()
    {
        try { _ = new UpkeepAlertTracker(new[] { 0, -5 }); return true; }
        catch (ArgumentException) { return false; }
    }

    [TestMethod]
    public void DurationsReadNaturally()
    {
        Assert.AreEqual("under 1m", UpkeepAlertTracker.FormatDuration(0));
        Assert.AreEqual("under 1m", UpkeepAlertTracker.FormatDuration(59));
        Assert.AreEqual("25m", UpkeepAlertTracker.FormatDuration(25 * Minute + 10));
        Assert.AreEqual("1h 42m", UpkeepAlertTracker.FormatDuration(Hour + 42 * Minute));
        Assert.AreEqual("30h 0m", UpkeepAlertTracker.FormatDuration(30 * Hour));
    }
}
