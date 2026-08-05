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
