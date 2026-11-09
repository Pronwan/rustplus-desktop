using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Send this server's Logic Engine rules and Device Automations up, so the cloud runs them
    /// while the app is closed.
    ///
    /// Always the whole set, never a change: a rule deleted here is simply absent from the next
    /// push, so there is no separate delete to lose. Sent rather than fetched, like the chat
    /// commands - the app is where they are edited, so it is the source of truth.
    ///
    /// The shape is written out here rather than serialising the models. The models carry UI
    /// state (expanded, confirming delete) the cloud has no use for, and a rename in the editor
    /// must not silently change what the worker reads. Two things are resolved on the way out
    /// because only the app knows them: a step aimed at a device group becomes the switches in
    /// it (the cloud sees devices, not groups), and an automation's location becomes the
    /// position the app captured when that device was paired.
    /// </summary>
    public static class CloudAutomationSync
    {
        /// <summary>Hashes of what was last sent, so an unchanged profile costs nothing.</summary>
        private static readonly Dictionary<string, string> LastSent = new();

        public static async Task SyncAsync(ServerProfile profile, string serverKey)
        {
            if (!CloudBackend.UsePlatform || !CloudAuthManager.IsAuthenticated) return;
            if (profile == null || string.IsNullOrWhiteSpace(serverKey)) return;

            object payload;
            try
            {
                payload = BuildPayload(profile, serverKey);
            }
            catch (Exception ex)
            {
                SupabaseAuthManager.AppendLog($"[Cloud automations] Could not read the rules: {ex.Message}");
                return;
            }

            var signature = System.Text.Json.JsonSerializer.Serialize(payload);

            lock (LastSent)
            {
                if (LastSent.TryGetValue(serverKey, out var previous) && previous == signature)
                    return;
            }

            try
            {
                await CloudApiClient.CallApiAsync("sync/automations", HttpMethod.Put, payload: payload);

                lock (LastSent)
                {
                    LastSent[serverKey] = signature;
                }
            }
            catch (Exception ex)
            {
                // Not fatal: the cloud keeps the last set it had, and the next save tries again.
                SupabaseAuthManager.AppendLog($"[Cloud automations] Could not sync rules: {ex.Message}");
            }
        }

        /// <summary>Forget what was sent, e.g. after signing out.</summary>
        public static void Reset()
        {
            lock (LastSent)
            {
                LastSent.Clear();
            }
        }

        internal static object BuildPayload(ServerProfile profile, string serverKey)
        {
            var devices = profile.Devices?.ToList() ?? new List<SmartDevice>();

            var automations = (profile.DeviceAutomationRules ?? new List<DeviceAutomationRule>()).ToList();
            var locations = new Dictionary<string, object>();
            foreach (var rule in automations)
            {
                if (rule.LocationEntityId == 0) continue;
                var device = Find(devices, rule.LocationEntityId);
                if (device?.PairedX is double x && device.PairedY is double y)
                    locations[rule.LocationEntityId.ToString(CultureInfo.InvariantCulture)] = new { x, y };
            }

            return new
            {
                server_key = serverKey,
                logic = new
                {
                    enabled = profile.IsLogicEngineActive,
                    rules = (profile.LogicRules ?? new List<LogicRule>()).Select(rule => new
                    {
                        id = rule.Id,
                        name = rule.Name,
                        enabled = rule.IsEnabled,
                        loop = new { enabled = rule.IsLoopEnabled, count = rule.LoopCount },
                        trigger = new
                        {
                            type = rule.TriggerType,
                            entity_id = rule.TriggerEntityId,
                            state = rule.TriggerState,
                            command = rule.TriggerCommand,
                            rule_id = rule.TriggerRuleId,
                        },
                        condition = new
                        {
                            @operator = rule.ConditionOperator ?? "NONE",
                            entity_id = rule.ConditionDeviceEntityId,
                            state = rule.ConditionDeviceState,
                        },
                        steps = Steps(rule.Steps, devices),
                    }).ToList(),
                },
                automations = new
                {
                    enabled = profile.IsDeviceAutomationActive,
                    rules = automations.Select(rule => new
                    {
                        id = rule.Id,
                        name = rule.Name,
                        enabled = rule.IsEnabled,
                        condition_type = rule.ConditionType,
                        player_match_mode = rule.PlayerMatchMode,
                        // A string: a steam id does not survive a JavaScript number.
                        specific_player_steam_id = rule.SpecificPlayerSteamId == 0
                            ? null
                            : rule.SpecificPlayerSteamId.ToString(CultureInfo.InvariantCulture),
                        location_entity_id = rule.LocationEntityId,
                        distance_meters = rule.DistanceMeters,
                        start_time = rule.StartTime,
                        end_time = rule.EndTime,
                        target_entity_id = rule.TargetEntityId,
                        matched_state = rule.MatchedState,
                        unmatched_state = rule.UnmatchedState,
                    }).ToList(),
                    device_locations = locations,
                },
            };
        }

        private static List<object> Steps(IEnumerable<LogicStep>? steps, List<SmartDevice> devices)
        {
            return (steps ?? Enumerable.Empty<LogicStep>()).Select(step => (object)new
            {
                type = step.StepType,
                wait_seconds = step.WaitSeconds,
                // Toggle: the switches this step flips. A group is resolved to its switches
                // here, the way the app resolves it when the step runs.
                target_entity_ids = Targets(step, devices),
                target_entity_id = step.TargetEntityId,
                toggle_state = step.ToggleState,
                // StartTimer
                timer_minutes = step.TimerMinutes,
                timer_target = step.TimerTarget,
                timer_name = step.TimerName,
                // CheckAvailability
                condition_operator = step.ConditionOperator,
                condition_device_ids = (step.ConditionDeviceIdsCsv ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => uint.TryParse(s.Trim(), out var id) ? id : 0u)
                    .Where(id => id != 0)
                    .ToList(),
                conditional_steps = Steps(step.ConditionalSteps, devices),
            }).ToList();
        }

        private static List<uint> Targets(LogicStep step, List<SmartDevice> devices)
        {
            if (step.StepType != "Toggle") return new List<uint>();

            if (!string.IsNullOrEmpty(step.TargetGroupName))
            {
                var group = devices.FirstOrDefault(d => d.IsGroup && d.Alias == step.TargetGroupName);
                return group?.Children == null
                    ? new List<uint>()
                    : Switches(group.Children).Select(d => d.EntityId).ToList();
            }

            return step.TargetEntityId == 0 ? new List<uint>() : new List<uint> { step.TargetEntityId };
        }

        private static IEnumerable<SmartDevice> Switches(IEnumerable<SmartDevice> devices)
        {
            foreach (var d in devices)
            {
                if (d.IsGroup && d.Children != null)
                {
                    foreach (var child in Switches(d.Children)) yield return child;
                }
                else if (string.Equals(d.Kind?.Replace(" ", ""), "SmartSwitch", StringComparison.OrdinalIgnoreCase))
                {
                    yield return d;
                }
            }
        }

        private static SmartDevice? Find(IEnumerable<SmartDevice> devices, uint id)
        {
            foreach (var d in devices)
            {
                if (!d.IsGroup && d.EntityId == id) return d;
                if (d.IsGroup && d.Children != null && Find(d.Children, id) is SmartDevice found) return found;
            }
            return null;
        }
    }
}
