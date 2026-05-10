using System;
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
            string msg = "Deep Sea event status unknown.";
            if (_deepSeaActive) 
            {
                msg = "Deep Sea is currently active!";
            }
            else if (_deepSeaDespawnTime.HasValue) 
            {
                var ago = DateTime.UtcNow - _deepSeaDespawnTime.Value;
                msg = $"Deep Sea ended {(int)ago.TotalMinutes} minutes ago.";
            }
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] DeepSea executed by {m.Author}");
            return;
        }

        // Command: Cargo
        if (cmd == profile.CmdCargo.ToLowerInvariant())
        {
            string msg = "Cargo Ship not active / remaining time unknown.";
            var activeCargo = _cargoDockStates.Values.FirstOrDefault();
            if (activeCargo != null)
            {
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
                    msg = "Cargo Ship active (remaining time unknown).";
                }
            }
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] Cargo executed by {m.Author}");
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
