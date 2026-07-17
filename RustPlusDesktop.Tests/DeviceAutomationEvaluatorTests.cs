using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class DeviceAutomationEvaluatorTests
{
    [TestMethod]
    public void ProximityUsesWorldMeterDistanceAndOnlineState()
    {
        var rule = new DeviceAutomationRule { DistanceMeters = 250, PlayerMatchMode = "AnyOnline" };
        var players = new[]
        {
            new DeviceAutomationEvaluator.PlayerSnapshot(1, true, 1200, 1000),
            new DeviceAutomationEvaluator.PlayerSnapshot(2, false, 1000, 1000)
        };

        Assert.IsTrue(DeviceAutomationEvaluator.IsProximityMatch(rule, 1000, 1000, players));
        rule.DistanceMeters = 150;
        Assert.IsFalse(DeviceAutomationEvaluator.IsProximityMatch(rule, 1000, 1000, players));
    }

    [TestMethod]
    public void OfflineModesDoNotRequirePositions()
    {
        var rule = new DeviceAutomationRule { PlayerMatchMode = "AllOffline" };
        var players = new[]
        {
            new DeviceAutomationEvaluator.PlayerSnapshot(1, false, null, null),
            new DeviceAutomationEvaluator.PlayerSnapshot(2, false, null, null)
        };

        Assert.IsTrue(DeviceAutomationEvaluator.IsProximityMatch(rule, 0, 0, players));
        rule.PlayerMatchMode = "AnyOnline";
        Assert.IsFalse(DeviceAutomationEvaluator.IsProximityMatch(rule, 0, 0, players));
    }

    [TestMethod]
    public void GameTimeSupportsWindowsAcrossMidnightAndRejectsUnknownTime()
    {
        var rule = new DeviceAutomationRule { StartTime = "20:00", EndTime = "08:00" };

        Assert.IsTrue(DeviceAutomationEvaluator.IsTimeMatch(rule, "23:30"));
        Assert.IsTrue(DeviceAutomationEvaluator.IsTimeMatch(rule, "07:59"));
        Assert.IsFalse(DeviceAutomationEvaluator.IsTimeMatch(rule, "12:00"));
        Assert.IsFalse(DeviceAutomationEvaluator.TryGetTimeMatch(rule, "–", out _));
    }
}
