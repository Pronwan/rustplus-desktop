using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void RecordOilRigAlarm(AlarmNotification alarm, string rigLabel)
    {
        var eventTime = alarm.EventTime ?? alarm.Timestamp;
        var profile = _vm?.Selected;

        // Dedup: check if an alarm for this rig on this server was recorded in the last 30s
        var existing = DockOilRigAlarms.FirstOrDefault(n =>
            (alarm.EntityId.HasValue && n.EntityId == alarm.EntityId) ||
            string.Equals(n.Title, rigLabel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Title, alarm.Title, StringComparison.OrdinalIgnoreCase));

        if (existing != null && Math.Abs((eventTime - existing.Timestamp).TotalSeconds) < 30)
            return;

        string label = rigLabel;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = alarm.Title ?? "Oil Rig";
        }

        NotificationCenterService.AddNotification(new RustPlusNotification(
            "OilRigAlarm", label, alarm.Message,
            alarm.Ip ?? profile?.Host, alarm.Port ?? profile?.Port, alarm.Server ?? profile?.Name)
        {
            Timestamp = eventTime,
            EntityId = alarm.EntityId,
            FcmNotificationId = alarm.FcmNotificationId
        });

        string canonicalRig = CanonicalRigName(label, alarm.Title, alarm.Message);
        _monumentWatcher.TriggerExternal(canonicalRig, 15 * 60, false);

        RefreshEventDock();
    }

    internal void RecordOilRigAlarmDirect(string rigName)
    {
        var profile = _vm?.Selected;
        string label = rigName.IndexOf("small", StringComparison.OrdinalIgnoreCase) >= 0
            ? Properties.Resources.UiBadgeSmallOil
            : Properties.Resources.UiBadgeLargeOil;

        var existing = DockOilRigAlarms.FirstOrDefault(n =>
            string.Equals(n.Title, label, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Title, rigName, StringComparison.OrdinalIgnoreCase));

        if (existing != null && Math.Abs((DateTime.UtcNow - existing.Timestamp.ToUniversalTime()).TotalSeconds) < 30)
            return;

        NotificationCenterService.AddNotification(new RustPlusNotification(
            "OilRigAlarm", label, $"{rigName} triggered",
            profile?.Host, profile?.Port, profile?.Name)
        {
            Timestamp = DateTime.UtcNow,
        });

        RefreshEventDock();
    }

    private static string CanonicalRigName(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            if (c.IndexOf("small", StringComparison.OrdinalIgnoreCase) >= 0) return "Small Oil Rig";
            if (c.IndexOf("large", StringComparison.OrdinalIgnoreCase) >= 0) return "Large Oil Rig";
        }
        return "Oil Rig";
    }

    private string GetOilRigKey(RustPlusNotification n)
    {
        if (n.EntityId.HasValue && _vm?.Selected?.Devices != null)
        {
            var targets = OilRigTriggerRegistry.TargetsForProfile(_vm.Selected);
            if (targets.TryGetValue(n.EntityId.Value, out var target))
            {
                if (target == "SmallOilRig") return "Small Oil Rig";
                if (target == "LargeOilRig") return "Large Oil Rig";
            }
        }

        return CanonicalRigName(n.Title, n.Message);
    }

    public IReadOnlyList<RustPlusNotification> DockOilRigAlarms => NotificationCenterService.Notifications
        .Where(n => n.Type == "OilRigAlarm" && _vm?.Selected is { } profile &&
            (!string.IsNullOrWhiteSpace(n.ServerIp)
                ? n.ServerIp == profile.Host && n.ServerPort == profile.Port
                : !string.IsNullOrWhiteSpace(n.ServerName) && n.ServerName == profile.Name))
        .OrderByDescending(n => n.Timestamp)
        .ToList();

    private string BuildSingleRigAnswer(string canonicalRigName, string label, RustPlusNotification? alarm)
    {
        var timeLeft = _monumentWatcher.GetActiveEventTimeLeft(canonicalRigName);
        if ((!timeLeft.HasValue || timeLeft.Value <= TimeSpan.Zero) && alarm != null)
        {
            var elapsed = DateTime.UtcNow - alarm.Timestamp.ToUniversalTime();
            if (elapsed.TotalSeconds >= 0 && elapsed.TotalMinutes < 15)
            {
                timeLeft = TimeSpan.FromMinutes(15) - elapsed;
            }
        }

        if (timeLeft.HasValue && timeLeft.Value > TimeSpan.Zero)
        {
            int minutes = (int)timeLeft.Value.TotalMinutes;
            int seconds = timeLeft.Value.Seconds;
            return string.Format(Properties.Resources.ChatCmdOilRigHackRunning, label, minutes, seconds);
        }

        if (alarm != null)
        {
            return $"{label}: {alarm.Timestamp.ToLocalTime():g}";
        }

        return $"{label}: {Properties.Resources.GetString("CodeUiNone") ?? "None"}";
    }

    internal string BuildOilRigAlarmAnswer()
    {
        var profile = _vm?.Selected;
        bool hasSmallRule = false;
        bool hasLargeRule = false;
        if (profile?.LogicRules != null)
        {
            foreach (var rule in profile.LogicRules.Where(r => r.IsEnabled))
            {
                foreach (var step in EnumerateSteps(rule))
                {
                    if (step.StepType == "StartTimer" && step.IsOilRigTimer)
                    {
                        if (step.TimerTarget == "SmallOilRig") hasSmallRule = true;
                        if (step.TimerTarget == "LargeOilRig") hasLargeRule = true;
                    }
                }
            }
        }

        var alarms = DockOilRigAlarms;
        var smallAlarm = alarms.FirstOrDefault(a => GetOilRigKey(a) == "Small Oil Rig");
        var largeAlarm = alarms.FirstOrDefault(a => GetOilRigKey(a) == "Large Oil Rig");
        var genericAlarm = alarms.FirstOrDefault(a => GetOilRigKey(a) == "Oil Rig");

        bool showBoth = (hasSmallRule && hasLargeRule) || (smallAlarm != null && largeAlarm != null);
        bool showSmall = hasSmallRule || smallAlarm != null;
        bool showLarge = hasLargeRule || largeAlarm != null;

        var parts = new List<string>();
        if (showBoth || (showSmall && showLarge))
        {
            parts.Add(BuildSingleRigAnswer("Small Oil Rig", Properties.Resources.UiBadgeSmallOil, smallAlarm));
            parts.Add(BuildSingleRigAnswer("Large Oil Rig", Properties.Resources.UiBadgeLargeOil, largeAlarm));
        }
        else if (showSmall)
        {
            parts.Add(BuildSingleRigAnswer("Small Oil Rig", Properties.Resources.UiBadgeSmallOil, smallAlarm));
        }
        else if (showLarge)
        {
            parts.Add(BuildSingleRigAnswer("Large Oil Rig", Properties.Resources.UiBadgeLargeOil, largeAlarm));
        }
        else if (genericAlarm != null)
        {
            parts.Add(BuildSingleRigAnswer("Oil Rig", "Oil Rig", genericAlarm));
        }
        else if (alarms.Count > 0)
        {
            var first = alarms[0];
            string key = GetOilRigKey(first);
            string lbl = key == "Small Oil Rig" ? Properties.Resources.UiBadgeSmallOil :
                         key == "Large Oil Rig" ? Properties.Resources.UiBadgeLargeOil : "Oil Rig";
            parts.Add(BuildSingleRigAnswer(key, lbl, first));
        }
        else
        {
            return "No Oil Rig Smart Alarm notification received.";
        }

        return string.Join(" | ", parts);
    }

    private EventDockItem CreateOilRigDockItem(string key, string name, string canonicalRigName, RustPlusNotification? alarm)
    {
        var timeLeft = _monumentWatcher.GetActiveEventTimeLeft(canonicalRigName);
        bool isActive = false;

        if (timeLeft.HasValue && timeLeft.Value > TimeSpan.Zero)
        {
            isActive = true;
        }
        else if (alarm != null)
        {
            var elapsed = DateTime.UtcNow - alarm.Timestamp.ToUniversalTime();
            if (elapsed.TotalSeconds >= 0 && elapsed.TotalMinutes < 15)
            {
                isActive = true;
            }
        }

        string timerText = alarm != null ? alarm.Timestamp.ToLocalTime().ToString("HH:mm:ss") : "—";
        string toolTip = BuildSingleRigAnswer(canonicalRigName, name, alarm);

        return new EventDockItem
        {
            Key = key,
            Name = name,
            Icon = "pack://application:,,,/Assets/icons/crate.png",
            Active = isActive,
            Trackable = false,
            TimerText = timerText,
            ToolTip = toolTip
        };
    }

    private List<EventDockItem> BuildOilRigAlarmDockItems()
    {
        var items = new List<EventDockItem>();
        var profile = _vm?.Selected;

        bool hasSmallRule = false;
        bool hasLargeRule = false;
        if (profile?.LogicRules != null)
        {
            foreach (var rule in profile.LogicRules.Where(r => r.IsEnabled))
            {
                foreach (var step in EnumerateSteps(rule))
                {
                    if (step.StepType == "StartTimer" && step.IsOilRigTimer)
                    {
                        if (step.TimerTarget == "SmallOilRig") hasSmallRule = true;
                        if (step.TimerTarget == "LargeOilRig") hasLargeRule = true;
                    }
                }
            }
        }

        var alarms = DockOilRigAlarms;
        var smallAlarm = alarms.FirstOrDefault(a => GetOilRigKey(a) == "Small Oil Rig");
        var largeAlarm = alarms.FirstOrDefault(a => GetOilRigKey(a) == "Large Oil Rig");
        var genericAlarm = alarms.FirstOrDefault(a => GetOilRigKey(a) == "Oil Rig");

        bool showBoth = (hasSmallRule && hasLargeRule) || (smallAlarm != null && largeAlarm != null);
        bool showSmall = hasSmallRule || smallAlarm != null;
        bool showLarge = hasLargeRule || largeAlarm != null;

        if (showBoth || (showSmall && showLarge))
        {
            items.Add(CreateOilRigDockItem("oilrig_small", Properties.Resources.UiBadgeSmallOil, "Small Oil Rig", smallAlarm));
            items.Add(CreateOilRigDockItem("oilrig_large", Properties.Resources.UiBadgeLargeOil, "Large Oil Rig", largeAlarm));
        }
        else if (showSmall)
        {
            items.Add(CreateOilRigDockItem("oilrig_small", Properties.Resources.UiBadgeSmallOil, "Small Oil Rig", smallAlarm));
        }
        else if (showLarge)
        {
            items.Add(CreateOilRigDockItem("oilrig_large", Properties.Resources.UiBadgeLargeOil, "Large Oil Rig", largeAlarm));
        }
        else
        {
            var latest = genericAlarm ?? alarms.FirstOrDefault();
            items.Add(CreateOilRigDockItem("oilrig", "Oil Rig Smart Alarm", "Oil Rig", latest));
        }

        return items;
    }

    /// <summary>
    /// Bridges the Logic Engine's oil rig timers back into the parts of the app that used to
    /// be fed by Chinook tracking.
    ///
    /// The rule is the same everywhere: an oil rig hack timer only counts as available if a
    /// rule is actually set up to start one. Showing a crate countdown command on a server
    /// where nothing can ever start a countdown is the failure this whole fallback work has
    /// been trying to avoid.
    /// </summary>
    internal static IEnumerable<LogicStep> EnumerateSteps(LogicRule rule)
    {
        foreach (var step in rule.Steps)
        {
            yield return step;

            // Conditional branches are steps too, and a timer inside one is no less real.
            foreach (var nested in step.ConditionalSteps)
                yield return nested;
        }
    }

    /// <summary>True when at least one enabled rule can start an oil rig hack timer.</summary>
    internal bool HasOilRigTimerRule()
    {
        var profile = _vm?.Selected;
        if (profile?.LogicRules == null) return false;

        return profile.LogicRules
            .Where(rule => rule.IsEnabled)
            .SelectMany(EnumerateSteps)
            .Any(step => step.StepType == "StartTimer" && step.IsOilRigTimer);
    }

    /// <summary>
    /// Re-reads the rules and tells the rest of the app whether crate alerts and the crate
    /// countdown command mean anything. Cheap enough to call whenever rules change.
    /// </summary>
    internal void RefreshOilRigTimerCapability()
    {
        // First, so that suppression is right even if one of the UI updates below throws.
        try { RebuildOilRigTriggerRegistry(); }
        catch { }

        try { EventCapabilities.SetOilRigTimers(HasOilRigTimerRule()); }
        catch { }

        try { ApplyOilRigTriggerBadges(); }
        catch { }
    }

    /// <summary>
    /// Rebuilds the cross-server trigger registry. Called once the saved profiles are loaded,
    /// before the push listener is hooked, and again whenever rules change.
    /// </summary>
    internal void RebuildOilRigTriggerRegistry() => OilRigTriggerRegistry.Rebuild(_vm?.Servers);

    /// <summary>
    /// Stamps the badge onto the matching devices and clears it everywhere else. Clearing is
    /// the half that matters: a device pulled out of the Logic Engine has to look and behave
    /// like an ordinary alarm again, without the user having to reconnect.
    /// </summary>
    private void ApplyOilRigTriggerBadges()
    {
        var profile = _vm?.Selected;
        if (profile?.Devices == null) return;

        var labels = OilRigTriggerRegistry.ForProfile(profile);
        var targets = OilRigTriggerRegistry.TargetsForProfile(profile);

        bool changed = false;

        void Walk(IEnumerable<SmartDevice> devices)
        {
            foreach (var device in devices)
            {
                device.OilRigBadge = labels.TryGetValue(device.EntityId, out var label) ? label : null;

                string? target = targets.TryGetValue(device.EntityId, out var t) ? t : null;
                if (device.OilRigTriggerTarget != target)
                {
                    device.OilRigTriggerTarget = target;
                    changed = true;
                }

                if (device.Children != null) Walk(device.Children);
            }
        }

        Walk(profile.Devices);

        // The worker only learns about rig triggers through the device sync, so a rule change
        // has to reach the cloud now rather than at the next routine upload — otherwise a
        // freshly configured trigger still rings the phone as a raid.
        if (changed) _ = UploadDevicesSnapshotForCurrentServerAsync();
    }


}
