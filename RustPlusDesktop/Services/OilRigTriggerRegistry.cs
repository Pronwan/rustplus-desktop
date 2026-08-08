using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

/// <summary>
/// Which Smart Alarms are wired to an oil rig, across every saved server.
///
/// Exists because the alarm suppression has to work before the app is connected to anything.
/// Push notifications arrive on their own schedule — a backlog lands seconds after launch —
/// and at that point there is no WebSocket, no team poll and no selected server. Deciding
/// "is this a rig trigger?" by walking the live view model made the answer depend on how far
/// startup had got, which is exactly why a rig alarm could still raise a raid alert.
///
/// Built from the saved profiles, which are loaded before the push listener starts, and
/// rebuilt whenever the rules change. Deliberately not a second persisted copy: the rules are
/// the truth, and a cached file would be free to disagree with them.
/// </summary>
public static class OilRigTriggerRegistry
{
    private static readonly object Gate = new();
    private static Dictionary<uint, string> _byId = new();
    private static Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Re-reads every profile's rules. Cheap; call it freely.</summary>
    public static void Rebuild(IEnumerable<ServerProfile?>? profiles)
    {
        var byId = new Dictionary<uint, string>();
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (profiles != null)
        {
            foreach (var profile in profiles)
            {
                if (profile?.LogicRules == null) continue;

                foreach (var rule in profile.LogicRules)
                {
                    if (!rule.IsEnabled) continue;
                    if (rule.TriggerType != "SmartAlarm" || rule.TriggerEntityId == 0) continue;

                    var step = EnumerateSteps(rule)
                        .FirstOrDefault(s => s.StepType == "StartTimer" && s.IsOilRigTimer);
                    if (step == null) continue;

                    string label = step.TimerTarget == "SmallOilRig"
                        ? Properties.Resources.UiBadgeSmallOil
                        : Properties.Resources.UiBadgeLargeOil;

                    byId[rule.TriggerEntityId] = label;

                    // The alarm's own text, for pushes that carry no entity ID. Not the
                    // device's name in the app: that comes from pairing and has nothing to do
                    // with what the alarm broadcasts.
                    if (IsDistinctive(step.AlarmTextHint))
                        byName[step.AlarmTextHint.Trim()] = label;
                }
            }
        }

        lock (Gate)
        {
            _byId = byId;
            _byName = byName;
        }
    }

    /// <summary>
    /// The rig label if this alarm is a rig trigger, else null.
    ///
    /// Entity ID first, since it is unambiguous. Falling back to the name is not a nicety:
    /// FCM pushes frequently carry no ID, and the existing recovery paths need either a
    /// WebSocket event from the last ten seconds or exactly one Smart Alarm to guess from —
    /// neither of which holds at startup, which is the case this whole class is for.
    /// </summary>
    public static string? Lookup(uint? entityId, params string?[] names)
    {
        lock (Gate)
        {
            if (entityId.HasValue && _byId.TryGetValue(entityId.Value, out var byId))
                return byId;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (_byName.TryGetValue(name.Trim(), out var byName)) return byName;
            }
        }

        return null;
    }

    /// <summary>All trigger devices on one profile, for the device-list badges.</summary>
    public static Dictionary<uint, string> ForProfile(ServerProfile? profile) =>
        TargetsForProfile(profile).ToDictionary(
            pair => pair.Key,
            pair => pair.Value == "SmallOilRig"
                ? Properties.Resources.UiBadgeSmallOil
                : Properties.Resources.UiBadgeLargeOil);

    /// <summary>
    /// The same devices, but keyed to the untranslated target — "SmallOilRig" or
    /// "LargeOilRig". This is what gets synced: a translated badge means nothing to the
    /// worker, and it would change under the user's language setting.
    /// </summary>
    public static Dictionary<uint, string> TargetsForProfile(ServerProfile? profile)
    {
        var map = new Dictionary<uint, string>();
        if (profile?.LogicRules == null) return map;

        foreach (var rule in profile.LogicRules)
        {
            if (!rule.IsEnabled) continue;
            if (rule.TriggerType != "SmartAlarm" || rule.TriggerEntityId == 0) continue;

            var step = EnumerateSteps(rule)
                .FirstOrDefault(s => s.StepType == "StartTimer" && s.IsOilRigTimer);
            if (step == null) continue;

            map[rule.TriggerEntityId] = step.TimerTarget;
        }

        return map;
    }

    /// <summary>
    /// Texts every unrenamed alarm sends. Matching on one of these would suppress every alarm
    /// on every server, which is the one failure this feature must never produce: a real raid
    /// alert swallowed. An alarm left at its default simply does not take part in text
    /// matching — it still works by entity ID, which is the normal case while connected.
    /// </summary>
    private static readonly HashSet<string> DefaultAlarmTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alarm", "Smart Alarm", "Alarm activated!", "Your base is under attack!",
        "Your base is under attack", "Base attacked", "Triggered",
    };

    private static bool IsDistinctive(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();

        // Two characters cannot identify anything, and a stray keystroke in the field must not
        // start matching real raid alerts.
        if (text.Length < 3) return false;

        return !DefaultAlarmTexts.Contains(text);
    }

    /// <summary>
    /// Records the text an alarm actually sends, once it has been identified by entity ID.
    ///
    /// Proven correct by definition, so it overwrites whatever was typed — a typo in the field
    /// repairs itself the first time the alarm fires while the app can see which device it is.
    /// </summary>
    /// <returns>True when something changed and the profiles want saving.</returns>
    public static bool LearnAlarmText(IEnumerable<ServerProfile?>? profiles, uint entityId,
                                      string? title)
    {
        // The title only, never the message. A Smart Alarm has two lines in-game and the push
        // sends the upper one as its title; the WebSocket event carries no title at all, so
        // accepting the message as a substitute meant the same alarm was learned twice with
        // different values in one firing, and whichever arrived last won. The push always
        // carries the title, so refusing the message costs nothing and removes the race.
        string? learned = IsDistinctive(title) ? title!.Trim() : null;
        if (learned == null || profiles == null) return false;

        bool changed = false;

        foreach (var profile in profiles)
        {
            if (profile?.LogicRules == null) continue;

            foreach (var rule in profile.LogicRules)
            {
                if (rule.TriggerType != "SmartAlarm" || rule.TriggerEntityId != entityId) continue;

                foreach (var step in EnumerateSteps(rule))
                {
                    if (step.StepType != "StartTimer" || !step.IsOilRigTimer) continue;
                    if (string.Equals(step.AlarmTextHint, learned, StringComparison.Ordinal)) continue;

                    step.AlarmTextHint = learned;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>Steps including those nested in conditional branches.</summary>
    private static IEnumerable<LogicStep> EnumerateSteps(LogicRule rule)
    {
        foreach (var step in rule.Steps)
        {
            yield return step;
            foreach (var nested in step.ConditionalSteps)
                yield return nested;
        }
    }

    private static SmartDevice? FindDevice(IEnumerable<SmartDevice>? devices, uint entityId)
    {
        if (devices == null) return null;

        foreach (var device in devices)
        {
            if (device.EntityId == entityId) return device;

            var child = FindDevice(device.Children, entityId);
            if (child != null) return child;
        }

        return null;
    }
}
