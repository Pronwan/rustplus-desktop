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
        _vm.Selected?.SyncChatCommands();
        ChatCommandsOverlay.Visibility = System.Windows.Visibility.Visible;
    }

    private void BtnCloseChatCommands_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ChatCommandsOverlay.Visibility = System.Windows.Visibility.Collapsed;
        _vm.Save(); // Save the new configuration settings
    }

    private DateTime _lastChatCommandTime = DateTime.MinValue;
    private const int ChatCommandCooldownSeconds = 2; // 2s cooldown for system stability

    private async Task ProcessChatCommands(TeamChatMessage m)
    {
        var profile = _vm.Selected;
        if (profile == null || !profile.ChatCommandsEnabled) return;

        var cmd = m.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(cmd) || !cmd.StartsWith("!")) return;

        // Global cooldown to prevent spam-induced API deadlocks
        if ((DateTime.UtcNow - _lastChatCommandTime).TotalSeconds < ChatCommandCooldownSeconds)
        {
            AppendLog($"[ChatCommand] Ignoring '{cmd}' from {m.Author} (Cooldown active)");
            return;
        }

        cmd = cmd.Substring(1); // Remove the '!' prefix for matching
        _lastChatCommandTime = DateTime.UtcNow;

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

        // Command: Switches (Dynamic List)
        foreach (var mapping in profile.SwitchCommandMappings)
        {
            if (cmd == mapping.Command.ToLowerInvariant() && mapping.EntityId != 0)
            {
                await ToggleCommandSwitch(real, mapping.EntityId, m.Author);
                return;
            }
        }

        // Command: Upkeep (Dynamic List)
        foreach (var mapping in profile.UpkeepCommandMappings)
        {
            if (cmd == mapping.Command.ToLowerInvariant() && mapping.EntityId != 0)
            {
                await ProcessUpkeepCommand(real, mapping.EntityId, m.Author);
                return;
            }
        }
    }

    private async Task ProcessUpkeepCommand(RustPlusClientReal real, uint entityId, string author)
    {
        var profile = _vm.Selected;
        if (profile == null) return;

        var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == entityId && (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor"));
        if (dev != null && dev.Storage?.IsToolCupboard == true)
        {
            var secs = dev.UpkeepSeconds ?? 0;
            if (secs <= 0)
            {
                _ = SendTeamChatSafeAsync($"Upkeep in {dev.PureName} TC: Empty or expired.");
            }
            else
            {
                int days = secs / 86400;
                int rem = secs % 86400;
                int hours = rem / 3600;
                rem = rem % 3600;
                int mins = rem / 60;

                string timeStr = "";
                if (days > 0) timeStr += $"{days} days, ";
                if (hours > 0 || days > 0) timeStr += $"{hours} hours, ";
                timeStr += $"{mins} minutes";

                var dailyMaterials = FormatUpkeepMaterialsPer24h(dev, secs);
                var materialsSuffix = string.IsNullOrWhiteSpace(dailyMaterials)
                    ? ""
                    : $" Need/24h: {dailyMaterials}.";

                _ = SendTeamChatSafeAsync($"Upkeep in {dev.PureName} TC: {timeStr}.{materialsSuffix}");
            }
            AppendLog($"[ChatCommand] Upkeep for {dev.Name} executed by {author}");
        }
        else
        {
            _ = SendTeamChatSafeAsync("Bound Tool Cupboard monitor not found or not paired.");
        }
    }

    private static string FormatUpkeepMaterialsPer24h(SmartDevice dev, int upkeepSeconds)
    {
        if (upkeepSeconds <= 0 || dev.Storage?.Items == null || dev.Storage.Items.Count == 0)
            return string.Empty;

        var parts = dev.Storage.Items
            .Where(IsUpkeepMaterial)
            .GroupBy(GetUpkeepMaterialKey)
            .Select(g =>
            {
                var sample = g.First();
                var amount = g.Sum(x => Math.Max(0, x.Amount));
                var per24h = (int)Math.Ceiling(amount * 86400.0 / upkeepSeconds);
                return new
                {
                    Sort = GetUpkeepMaterialSort(sample),
                    Name = GetShortUpkeepMaterialName(sample),
                    Amount = per24h
                };
            })
            .Where(x => x.Amount > 0)
            .OrderBy(x => x.Sort)
            .Select(x => $"{x.Name} {x.Amount:N0}".Replace(",", ""))
            .ToList();

        return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
    }

    private static bool IsUpkeepMaterial(StorageItemVM item)
    {
        var shortName = (item.ShortName ?? string.Empty).Trim().ToLowerInvariant();
        if (shortName is "wood" or "stones" or "metal.fragments" or "metal.refined")
            return true;

        // do not touch this mf hardcoded item ID list, it's the only way to reliably identify these items for upkeep calculations without false positives from modded items with similar names
        return item.ItemId is -151838493 or -2099697608 or 69511070 or 317398316;
    }

    private static string GetUpkeepMaterialKey(StorageItemVM item)
    {
        var shortName = (item.ShortName ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(shortName)) return shortName;
        return item.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int GetUpkeepMaterialSort(StorageItemVM item)
    {
        var shortName = (item.ShortName ?? string.Empty).Trim().ToLowerInvariant();
        return shortName switch
        {
            "wood" => 10,
            "stones" => 20,
            "metal.fragments" => 30,
            "metal.refined" => 40,
            _ => item.ItemId switch
            {
                -151838493 => 10,
                -2099697608 => 20,
                69511070 => 30,
                317398316 => 40,
                _ => 100
            }
        };
    }

    private static string GetShortUpkeepMaterialName(StorageItemVM item)
    {
        var shortName = (item.ShortName ?? string.Empty).Trim().ToLowerInvariant();
        return shortName switch
        {
            "wood" => "Wood",
            "stones" => "Stone",
            "metal.fragments" => "Metal",
            "metal.refined" => "HQM",
            _ => item.ItemId switch
            {
                -151838493 => "Wood",
                -2099697608 => "Stone",
                69511070 => "Metal",
                317398316 => "HQM",
                _ => MainWindow.ResolveItemName(item.ItemId, item.ShortName)
            }
        };
    }

    private async Task ToggleCommandSwitch(RustPlusClientReal real, uint entityId, string author)
    {
        var profile = _vm.Selected;
        if (profile == null) return;
        
        var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == entityId && d.Kind == "SmartSwitch");
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
