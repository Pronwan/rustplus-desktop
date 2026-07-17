using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private readonly SemaphoreSlim _deviceAutomationGate = new(1, 1);
    private bool _deviceAutomationRunningAction;

    private async Task CapturePairedDeviceLocationAsync(ServerProfile profile, SmartDevice device, string steamIdText)
    {
        if (!ReferenceEquals(_vm.Selected, profile)
            || !profile.IsFullConnected
            || _real is not RustPlusClientReal real
            || !ulong.TryParse(steamIdText, out ulong steamId))
            return;

        RustPlusClientReal.TeamInfo.Member? player = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var team = await real.GetTeamInfoAsync(cts.Token);
            player = team?.Members.FirstOrDefault(member => member.SteamId == steamId);
        }
        catch (Exception ex)
        {
            AppendLog($"[DeviceAutomation] Could not refresh pairing position: {ex.Message}");
        }

        player ??= TeamMembers
            .Where(member => member.SteamId == steamId)
            .Select(member => new RustPlusClientReal.TeamInfo.Member
            {
                SteamId = member.SteamId,
                Online = member.IsOnline,
                X = member.X,
                Y = member.Y
            })
            .FirstOrDefault();

        if (player is not { Online: true, X: not null, Y: not null })
        {
            AppendLog($"[DeviceAutomation] Pairing player {steamId} has no live position; device #{device.EntityId} location was not captured.");
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            device.PairedX = player.X;
            device.PairedY = player.Y;
            device.PairedBySteamId = steamId;
            device.PairedLocationCapturedAt = DateTime.UtcNow;
            _vm.Save();
            DeviceAutomationPanel?.RefreshListBindings();
            AppendLog($"[DeviceAutomation] Captured device #{device.EntityId} at X={player.X:0.0}, Y={player.Y:0.0} from player {steamId}.");
        });
    }

    private async Task EvaluateDeviceAutomationAsync()
    {
        var profile = _vm.Selected;
        if (profile is not { IsDeviceAutomationActive: true, IsFullConnected: true }
            || profile.DeviceAutomationRules.Count == 0
            || !await _deviceAutomationGate.WaitAsync(0))
            return;

        try
        {
            var players = TeamMembers
                .Select(player => new DeviceAutomationEvaluator.PlayerSnapshot(
                    player.SteamId, player.IsOnline && !player.IsDead, player.X, player.Y))
                .ToList();
            var decisions = new List<(DeviceAutomationRule Rule, SmartDevice Target, bool State)>();

            foreach (var rule in profile.DeviceAutomationRules.Where(rule => rule.IsEnabled))
            {
                bool matched;
                if (rule.ConditionType == "PlayerProximity")
                {
                    double x = 0, y = 0;
                    if (!rule.PlayerMatchMode.EndsWith("Offline", StringComparison.Ordinal))
                    {
                        var location = FindDeviceById(profile.Devices, rule.LocationEntityId);
                        if (location?.PairedX is not double pairedX || location.PairedY is not double pairedY) continue;
                        x = pairedX;
                        y = pairedY;
                    }
                    matched = DeviceAutomationEvaluator.IsProximityMatch(rule, x, y, players);
                }
                else if (rule.ConditionType == "GameTime")
                {
                    if (!DeviceAutomationEvaluator.TryGetTimeMatch(rule, _vm.ServerTime, out matched)) continue;
                }
                else
                {
                    continue;
                }

                var target = FindDeviceById(profile.Devices, rule.TargetEntityId);
                if (target == null
                    || target.IsMissing
                    || !string.Equals(target.Kind?.Replace(" ", ""), "SmartSwitch", StringComparison.OrdinalIgnoreCase))
                    continue;
                decisions.Add((rule, target, matched ? rule.MatchedState : rule.UnmatchedState));
            }

            foreach (var targetGroup in decisions.GroupBy(decision => decision.Target.EntityId))
            {
                var states = targetGroup.Select(decision => decision.State).Distinct().ToList();
                if (states.Count != 1)
                {
                    AppendLog($"[DeviceAutomation] Conflicting rules for device #{targetGroup.Key}; no action taken.");
                    continue;
                }

                var decision = targetGroup.First();
                if (decision.Target.IsOn == decision.State) continue;
                if (_globalToggleBusy || _logicEngineRunningAction) continue;

                _deviceAutomationRunningAction = true;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await _rust.ToggleSmartSwitchAsync(decision.Target.EntityId, decision.State, cts.Token);
                    decision.Target.IsOn = decision.State;
                    decision.Rule.LastAppliedState = decision.State;
                    AppendLog($"[DeviceAutomation] '{decision.Rule.Name}' set {decision.Target.DisplayName} {(decision.State ? "ON" : "OFF")}.");
                    await Task.Delay(800);
                }
                catch (Exception ex)
                {
                    AppendLog($"[DeviceAutomation] '{decision.Rule.Name}' failed: {ex.Message}");
                }
                finally
                {
                    _deviceAutomationRunningAction = false;
                }
            }
        }
        finally
        {
            _deviceAutomationGate.Release();
        }
    }
}
