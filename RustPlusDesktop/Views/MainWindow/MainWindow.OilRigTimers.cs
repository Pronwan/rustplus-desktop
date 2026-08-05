using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
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
        try { EventCapabilities.SetOilRigTimers(HasOilRigTimerRule()); }
        catch { }

        try { ApplyOilRigTriggerBadges(); }
        catch { }
    }

    /// <summary>
    /// Smart Alarms that a rule uses as an oil rig trigger, mapped to the rig they watch.
    ///
    /// Only enabled rules count, and only ones whose trigger really is a Smart Alarm — a rule
    /// fired by a chat command has no device whose alarm should fall silent.
    /// </summary>
    private Dictionary<uint, string> BuildOilRigTriggerMap()
    {
        var map = new Dictionary<uint, string>();

        var profile = _vm?.Selected;
        if (profile?.LogicRules == null) return map;

        foreach (var rule in profile.LogicRules)
        {
            if (!rule.IsEnabled) continue;
            if (rule.TriggerType != "SmartAlarm" || rule.TriggerEntityId == 0) continue;

            var step = EnumerateSteps(rule)
                .FirstOrDefault(s => s.StepType == "StartTimer" && s.IsOilRigTimer);
            if (step == null) continue;

            map[rule.TriggerEntityId] = step.TimerTarget == "SmallOilRig"
                ? Properties.Resources.UiBadgeSmallOil
                : Properties.Resources.UiBadgeLargeOil;
        }

        return map;
    }

    /// <summary>The rig label if this alarm is an oil rig trigger, else null.</summary>
    private string? GetOilRigTriggerLabel(uint entityId)
    {
        // Every server's rules, not just the selected one: an alarm push can arrive for a
        // server the user is not currently looking at, and it is just as much a rig trigger.
        foreach (var profile in _vm.Servers)
        {
            if (profile?.LogicRules == null) continue;

            foreach (var rule in profile.LogicRules)
            {
                if (!rule.IsEnabled) continue;
                if (rule.TriggerType != "SmartAlarm" || rule.TriggerEntityId != entityId) continue;

                var step = EnumerateSteps(rule)
                    .FirstOrDefault(s => s.StepType == "StartTimer" && s.IsOilRigTimer);
                if (step == null) continue;

                return step.TimerTarget == "SmallOilRig"
                    ? Properties.Resources.UiBadgeSmallOil
                    : Properties.Resources.UiBadgeLargeOil;
            }
        }

        return null;
    }

    /// <summary>
    /// Stamps the badge onto the matching devices and clears it everywhere else. Clearing is
    /// the half that matters: a device pulled out of the Logic Engine has to look and behave
    /// like an ordinary alarm again, without the user having to reconnect.
    /// </summary>
    private void ApplyOilRigTriggerBadges()
    {
        var profile = _vm?.Selected;
        if (profile?.Devices == null) return;

        var map = BuildOilRigTriggerMap();

        void Walk(IEnumerable<SmartDevice> devices)
        {
            foreach (var device in devices)
            {
                device.OilRigBadge = map.TryGetValue(device.EntityId, out var label) ? label : null;
                if (device.Children != null) Walk(device.Children);
            }
        }

        Walk(profile.Devices);
    }

    /// <summary>
    /// The countdown answer for the chat command, or null when no rig has one running.
    /// Reports every rig that is being hacked, since both can run at once.
    /// </summary>
    private string? BuildOilRigTimerAnswer()
    {
        if (!HasOilRigTimerRule()) return null;

        var parts = new List<string>();
        foreach (var (key, label) in new[]
                 {
                     ("Small Oil Rig", Properties.Resources.SmallOilRig),
                     ("Large Oil Rig", Properties.Resources.LargeOilRig),
                 })
        {
            var left = _monumentWatcher.GetActiveEventTimeLeft(key);
            if (left == null || left.Value <= TimeSpan.Zero) continue;

            parts.Add(string.Format(
                Properties.Resources.ChatCmdOilRigHackRunning,
                label, (int)left.Value.TotalMinutes, left.Value.Seconds));
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }
}
