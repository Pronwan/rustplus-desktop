using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;


namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void BtnOpenChatCommands_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ChatCommandsOverlay.Visibility = System.Windows.Visibility.Visible;
    }

    private void BtnCloseChatCommands_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ChatCommandsOverlay.Visibility = System.Windows.Visibility.Collapsed;
        _vm.Save(); // Save the new configuration settings
    }

    private async Task ProcessChatCommands(TeamChatMessage m)
    {
        var profile = _vm.Selected;
        if (profile == null || !profile.ChatCommandsEnabled) return;

        var cmd = m.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(cmd) || !cmd.StartsWith("!")) return;
        cmd = cmd.Substring(1); // Remove the '!' prefix for matching

        if (_rust is not RustPlusClientReal real) return;

        // Command: Pop
        if (cmd == profile.CmdPop.ToLowerInvariant())
        {
            string qText = _vm.ServerQueue != "0" && _vm.ServerQueue != "-" ? $" Queue: {_vm.ServerQueue} players." : "";
            string msg = $"{_vm.ServerPlayers} players currently online.{qText}";
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] Pop executed by {m.Author}");
            return;
        }

        // Command: Time
        if (cmd == profile.CmdTime.ToLowerInvariant())
        {
            string msg = $"Current in-game time: {_vm.ServerTime}.";
            if (!string.IsNullOrWhiteSpace(_vm.TimeUntilNextPhase))
            {
                msg += $" ({_vm.TimeUntilNextPhase})";
            }
            _ = SendTeamChatSafeAsync(msg.Trim());
            AppendLog($"[ChatCommand] Time executed by {m.Author}");
            return;
        }

        // Command: Promote
        if (cmd == profile.CmdPromote.ToLowerInvariant())
        {
            _ = real.PromoteToLeaderAsync(m.SteamId);
            _ = SendTeamChatSafeAsync($"{m.Author} was promoted to leader.");
            AppendLog($"[ChatCommand] Promote executed by {m.Author}");
            return;
        }

        // Command: Deep Sea
        if (cmd == profile.CmdDeepSea.ToLowerInvariant())
        {
            string msg;
            if (_deepSeaActive)
            {
                if (_deepSeaSpawnTime.HasValue)
                {
                    var elapsed = DateTime.UtcNow - _deepSeaSpawnTime.Value;
                    msg = $"Deep Sea is active! Running for {(int)elapsed.TotalHours}h {elapsed.Minutes}m.";
                }
                else
                {
                    msg = "Deep Sea is currently active (connected mid-event – spawn time unknown).";
                }
            }
            else if (_deepSeaDespawnTime.HasValue)
            {
                var ago = DateTime.UtcNow - _deepSeaDespawnTime.Value;
                msg = $"Deep Sea ended {(int)ago.TotalMinutes} minutes ago this session.";
            }
            else
            {
                msg = "Deep Sea event status unknown (not seen this session).";
            }
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] DeepSea executed by {m.Author}");
            return;
        }

        // Command: Cargo
        if (cmd == profile.CmdCargo.ToLowerInvariant())
        {
            string msg = "Cargo Ship not active.";
            var activeCargo = _cargoDockStates.Values.FirstOrDefault();
            if (activeCargo != null)
            {
                if (activeCargo.IsDocked && activeCargo.DockTime.HasValue)
                {
                    int dockDuration = TrackingService.GetLearnedDockingDuration(profile.Host);
                    if (dockDuration > 0 && !activeCargo.WasAlreadyDocked)
                    {
                        var dockRemain = TimeSpan.FromMinutes(dockDuration) - (DateTime.UtcNow - activeCargo.DockTime.Value);
                        if (dockRemain.TotalMinutes > 0)
                            msg = $"Cargo Ship docked at {activeCargo.HarborName ?? "harbor"}. Departs in approx. {(int)dockRemain.TotalMinutes} minutes.";
                        else
                            msg = $"Cargo Ship docked at {activeCargo.HarborName ?? "harbor"} and preparing to depart.";
                    }
                    else
                    {
                        msg = $"Cargo Ship docked at {activeCargo.HarborName ?? "harbor"} (departure time unknown).";
                    }
                }
                else if (activeCargo.SeenAtEdge)
                {
                    // We saw the spawn this session — time estimate is reliable
                    int fullLife = TrackingService.GetLearnedCargoFullLife(profile.Host);
                    if (fullLife > 0 && activeCargo.FirstSeen.HasValue)
                    {
                        var remain = TimeSpan.FromMinutes(fullLife) - (DateTime.UtcNow - activeCargo.FirstSeen.Value);
                        if (remain.TotalMinutes > 0)
                            msg = $"Cargo Ship active. Leaves in approx. {(int)remain.TotalMinutes} minutes.";
                        else
                            msg = "Cargo Ship active and preparing to leave soon.";
                    }
                    else
                    {
                        msg = "Cargo Ship active (route duration not yet learned for this server).";
                    }
                }
                else
                {
                    // Mid-connect — we don't know how long it's been on the map
                    msg = "Cargo Ship active (connected after spawn – remaining time unknown).";
                }
            }
            else if (_cargoLastDespawnUtc.HasValue)
            {
                var ago = DateTime.UtcNow - _cargoLastDespawnUtc.Value;
                msg = $"Cargo Ship not active. Despawned {(int)ago.TotalMinutes} minutes ago this session.";
            }
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] Cargo executed by {m.Author}");
            return;
        }

        // Command: Oil Rig
        if (cmd == profile.CmdOilRig.ToLowerInvariant())
        {
            var parts = new List<string>();
            foreach (var rigName in new[] { "Small Oil Rig", "Large Oil Rig" })
            {
                var timeLeft = _monumentWatcher.GetActiveEventTimeLeft(rigName);
                if (timeLeft.HasValue)
                {
                    parts.Add($"{rigName}: crate in {(int)timeLeft.Value.TotalMinutes}m {timeLeft.Value.Seconds}s");
                }
                else
                {
                    var lastTrig = _monumentWatcher.GetLastTriggered(rigName);
                    if (lastTrig.HasValue)
                    {
                        var ago = DateTime.UtcNow - lastTrig.Value;
                        parts.Add($"{rigName}: last called {(int)ago.TotalMinutes}m ago");
                    }
                    else
                    {
                        parts.Add($"{rigName}: not called this session");
                    }
                }
            }
            _ = SendTeamChatSafeAsync(string.Join(" | ", parts));
            AppendLog($"[ChatCommand] OilRig executed by {m.Author}");
            return;
        }

        // Command: Switch 1
        if (cmd == profile.CmdSwitch1.ToLowerInvariant() && profile.BoundSwitchId1.HasValue)
        {
            await ToggleCommandSwitch(real, profile.BoundSwitchId1.Value, m.Author);
            return;
        }

        // Command: Switch 2
        if (cmd == profile.CmdSwitch2.ToLowerInvariant() && profile.BoundSwitchId2.HasValue)
        {
            await ToggleCommandSwitch(real, profile.BoundSwitchId2.Value, m.Author);
            return;
        }
    }

    private async Task ToggleCommandSwitch(RustPlusClientReal real, uint entityId, string author)
    {
        var profile = _vm.Selected;
        if (profile == null) return;
        
        var dev = profile.Devices.FirstOrDefault(d => d.EntityId == entityId && d.Kind == "SmartSwitch");
        if (dev != null)
        {
            bool newState = !(dev.IsOn ?? false);
            try
            {
                await real.ToggleSmartSwitchAsync(entityId, newState);
                _ = SendTeamChatSafeAsync($"{dev.Name} turned {(newState ? "ON" : "OFF")}.");
                AppendLog($"[ChatCommand] {dev.Name} toggled to {newState} by {author}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ChatCommand] Failed to toggle {dev.Name}: {ex.Message}");
            }
        }
        else
        {
            _ = SendTeamChatSafeAsync("Bound Smart Switch not found or not paired.");
        }
    }
}
