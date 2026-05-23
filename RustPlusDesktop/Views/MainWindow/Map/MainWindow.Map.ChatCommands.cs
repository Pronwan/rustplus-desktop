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
        if (_chatOpenedForCommandsOnly)
        {
            _chatOpenedForCommandsOnly = false;
            ChatContentBorder.Visibility = System.Windows.Visibility.Collapsed;
        }
    }

    private DateTime _lastChatCommandTime = DateTime.MinValue;
    private const int ChatCommandCooldownSeconds = 2; // 2s cooldown for system stability

    private async Task ProcessChatCommands(TeamChatMessage m)
    {
        var profile = _vm.Selected;
        if (profile == null || !profile.ChatCommandsEnabled) return;

        string prefix = profile.ChatCommandPrefix;
        if (string.IsNullOrEmpty(prefix)) prefix = "!";

        var cmd = m.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(cmd) || !cmd.StartsWith(prefix)) return;

        // Global cooldown to prevent spam-induced API deadlocks
        if ((DateTime.UtcNow - _lastChatCommandTime).TotalSeconds < ChatCommandCooldownSeconds)
        {
            AppendLog($"[ChatCommand] Ignoring '{cmd}' from {m.Author} (Cooldown active)");
            return;
        }

        cmd = cmd.Substring(prefix.Length); // Remove prefix for matching
        _lastChatCommandTime = DateTime.UtcNow;

        if (_rust is not RustPlusClientReal real) return;

        // Command: List Commands
        if (cmd == profile.CmdList.ToLowerInvariant())
        {
            var standardCmds = new List<string>();
            if (!string.IsNullOrWhiteSpace(profile.CmdPop)) standardCmds.Add(prefix + profile.CmdPop);
            if (!string.IsNullOrWhiteSpace(profile.CmdTime)) standardCmds.Add(prefix + profile.CmdTime);
            if (!string.IsNullOrWhiteSpace(profile.CmdPromote)) standardCmds.Add(prefix + profile.CmdPromote);
            if (!string.IsNullOrWhiteSpace(profile.CmdDeepSea)) standardCmds.Add(prefix + profile.CmdDeepSea);
            if (!string.IsNullOrWhiteSpace(profile.CmdCargo)) standardCmds.Add(prefix + profile.CmdCargo);
            if (!string.IsNullOrWhiteSpace(profile.CmdOilRig)) standardCmds.Add(prefix + profile.CmdOilRig);
            if (!string.IsNullOrWhiteSpace(profile.CmdHeli)) standardCmds.Add(prefix + profile.CmdHeli);
            if (!string.IsNullOrWhiteSpace(profile.CmdVendor)) standardCmds.Add(prefix + profile.CmdVendor);
            if (!string.IsNullOrWhiteSpace(profile.CmdUpkeepDetail)) standardCmds.Add(prefix + profile.CmdUpkeepDetail);

            string standardMsg = string.Format(Properties.Resources.ChatCmdListHeader, string.Join(", ", standardCmds));
            if (standardMsg.Length > 128) standardMsg = standardMsg.Substring(0, 125) + "...";
            _ = SendTeamChatSafeAsync(standardMsg);

            var deviceCmds = new List<string>();
            foreach (var mapping in profile.SwitchCommandMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.Command) && mapping.EntityId != 0)
                {
                    var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == mapping.EntityId && d.Kind == "SmartSwitch");
                    if (dev != null) deviceCmds.Add($"[{dev.PureName}]: {prefix}{mapping.Command}");
                }
            }
            foreach (var mapping in profile.UpkeepCommandMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.Command) && mapping.EntityId != 0)
                {
                    var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == mapping.EntityId && (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor"));
                    if (dev != null) deviceCmds.Add($"[{dev.PureName}]: {prefix}{mapping.Command}");
                }
            }

            if (deviceCmds.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    string devMsg = string.Join(" | ", deviceCmds);
                    if (devMsg.Length > 128) devMsg = devMsg.Substring(0, 125) + "...";
                    await SendTeamChatSafeAsync(devMsg);
                });
            }

            AppendLog($"[ChatCommand] List executed by {m.Author}");
            return;
        }

        // Command: Pop
        if (cmd == profile.CmdPop.ToLowerInvariant())
        {
            string qText = _vm.ServerQueue != "0" && _vm.ServerQueue != "-" ? string.Format(Properties.Resources.ChatCmdPopQueue, _vm.ServerQueue) : "";
            string msg = string.Format(Properties.Resources.ChatCmdPopResponse, _vm.ServerPlayers, qText);
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] Pop executed by {m.Author}");
            return;
        }

        // Command: Time
        if (cmd == profile.CmdTime.ToLowerInvariant())
        {
            string msg = string.Format(Properties.Resources.ChatCmdTimeResponse, _vm.ServerTime);
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
            _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.ChatCmdPromoteResponse, m.Author));
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
                    msg = string.Format(Properties.Resources.ChatCmdDeepSeaActive, FormatAgo(elapsed));
                }
                else
                {
                    msg = Properties.Resources.ChatCmdDeepSeaActiveMidEvent;
                }
            }
            else if (_deepSeaDespawnTime.HasValue)
            {
                var ago = DateTime.UtcNow - _deepSeaDespawnTime.Value;
                msg = string.Format(Properties.Resources.ChatCmdDeepSeaEndedMinutesAgo, (int)ago.TotalMinutes);
            }
            else
            {
                msg = Properties.Resources.ChatCmdDeepSeaStatusUnknown;
            }
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] DeepSea executed by {m.Author}");
            return;
        }

        // Command: Cargo
        if (cmd == profile.CmdCargo.ToLowerInvariant())
        {
            string msg = Properties.Resources.ChatCmdCargoNotActive;
            var activeCargo = _cargoDockStates.Values.FirstOrDefault();
            if (activeCargo != null)
            {
                string harborName = activeCargo.HarborName ?? Properties.Resources.HarborFallback;
                if (activeCargo.IsDocked && activeCargo.DockTime.HasValue)
                {
                    int dockDuration = TrackingService.GetLearnedDockingDuration(profile.Host);
                    if (dockDuration > 0 && !activeCargo.WasAlreadyDocked)
                    {
                        var dockRemain = TimeSpan.FromMinutes(dockDuration) - (DateTime.UtcNow - activeCargo.DockTime.Value);
                        if (dockRemain.TotalMinutes > 0)
                            msg = string.Format(Properties.Resources.ChatCmdCargoDockedDeparts, harborName, (int)dockRemain.TotalMinutes);
                        else
                            msg = string.Format(Properties.Resources.ChatCmdCargoDockedPreparingDepart, harborName);
                    }
                    else
                    {
                        msg = string.Format(Properties.Resources.ChatCmdCargoDockedUnknown, harborName);
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
                            msg = string.Format(Properties.Resources.ChatCmdCargoActiveLeaves, (int)remain.TotalMinutes);
                        else
                            msg = Properties.Resources.ChatCmdCargoActivePreparingLeave;
                    }
                    else
                    {
                        msg = Properties.Resources.ChatCmdCargoActiveDurationNotLearned;
                    }
                }
                else
                {
                    // Mid-connect — we don't know how long it's been on the map
                    msg = Properties.Resources.ChatCmdCargoActiveMidRoute;
                }
            }
            else if (_cargoLastDespawnUtc.HasValue)
            {
                var ago = DateTime.UtcNow - _cargoLastDespawnUtc.Value;
                msg = string.Format(Properties.Resources.ChatCmdCargoDespawnedMinutesAgo, (int)ago.TotalMinutes);
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
                    parts.Add(string.Format(Properties.Resources.ChatCmdOilRigCrateIn, rigName, (int)timeLeft.Value.TotalMinutes, timeLeft.Value.Seconds));
                }
                else
                {
                    var lastTrig = _monumentWatcher.GetLastTriggered(rigName);
                    if (lastTrig.HasValue)
                    {
                        var ago = DateTime.UtcNow - lastTrig.Value;
                        parts.Add(string.Format(Properties.Resources.ChatCmdOilRigLastCalledAgo, rigName, (int)ago.TotalMinutes));
                    }
                    else
                    {
                        parts.Add(string.Format(Properties.Resources.ChatCmdOilRigNotCalled, rigName));
                    }
                }
            }
            _ = SendTeamChatSafeAsync(string.Join(" | ", parts));
            AppendLog($"[ChatCommand] OilRig executed by {m.Author}");
            return;
        }

        // Command: Patrol Heli
        if (cmd == profile.CmdHeli.ToLowerInvariant())
        {
            string msg;
            bool isHeliActive = _dynStates.Values.Any(s => s.Type == 8);
            if (isHeliActive)
            {
                if (_heliSpawnTime.HasValue)
                {
                    var elapsed = DateTime.UtcNow - _heliSpawnTime.Value;
                    msg = string.Format(Properties.Resources.ChatCmdHeliActive, FormatAgo(elapsed));
                }
                else
                {
                    msg = Properties.Resources.ChatCmdHeliActiveMidEvent;
                }
            }
            else if (_heliLastEventUtc.HasValue)
            {
                var ago = DateTime.UtcNow - _heliLastEventUtc.Value;
                string reason = _heliLastEventWasCrash ? Properties.Resources.ChatCmdHeliReasonShotDown : Properties.Resources.ChatCmdHeliReasonLeftMap;
                msg = string.Format(Properties.Resources.ChatCmdHeliNotActiveAgo, reason, FormatAgo(ago));
            }
            else
            {
                msg = Properties.Resources.ChatCmdHeliStatusUnknown;
            }
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] Heli executed by {m.Author}");
            return;
        }

        // Command: Travelling Vendor
        if (cmd == profile.CmdVendor.ToLowerInvariant())
        {
            string msg;
            bool isVendorActive = _dynStates.Values.Any(s => s.Type == 6);
            if (isVendorActive)
            {
                if (_vendorSpawnTime.HasValue)
                {
                    var elapsed = DateTime.UtcNow - _vendorSpawnTime.Value;
                    msg = string.Format(Properties.Resources.ChatCmdVendorActive, FormatAgo(elapsed));
                }
                else
                {
                    msg = Properties.Resources.ChatCmdVendorActiveMidEvent;
                }
            }
            else if (_vendorDespawnTime.HasValue)
            {
                var ago = DateTime.UtcNow - _vendorDespawnTime.Value;
                msg = string.Format(Properties.Resources.ChatCmdVendorDespawnedAgo, FormatAgo(ago));
            }
            else
            {
                msg = Properties.Resources.ChatCmdVendorStatusUnknown;
            }
            _ = SendTeamChatSafeAsync(msg);
            AppendLog($"[ChatCommand] Vendor executed by {m.Author}");
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

        // Command: Detailed Upkeep (Global)
        if (cmd == profile.CmdUpkeepDetail.ToLowerInvariant())
        {
            var tcs = profile.UpkeepCommandMappings.Where(mapping => mapping.EntityId != 0).ToList();
            if (tcs.Count == 0)
            {
                _ = SendTeamChatSafeAsync(Properties.Resources.ChatCmdUpkeepNoTcMapped);
            }
            else
            {
                bool first = true;
                foreach (var mapping in tcs)
                {
                    var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == mapping.EntityId && (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor"));
                    if (dev != null && (dev.Storage == null || dev.Storage.IsToolCupboard || dev.Storage.ItemsCount == 0))
                    {
                        if (!first)
                        {
                            await Task.Delay(profile.ChatCommandDelaySeconds * 1000);
                        }
                        first = false;

                        var secs = dev.UpkeepSeconds ?? 0;
                        if (secs <= 0)
                        {
                            _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcEmptyExpired, dev.PureName));
                        }
                        else
                        {
                            int days = secs / 86400;
                            int rem = secs % 86400;
                            int hours = rem / 3600;
                            rem = rem % 3600;
                            int mins = rem / 60;

                            var timeParts = new List<string>();
                            if (days > 0) timeParts.Add(string.Format(Properties.Resources.ChatCmdUpkeepDays, days));
                            if (hours > 0 || days > 0) timeParts.Add(string.Format(Properties.Resources.ChatCmdUpkeepHours, hours));
                            timeParts.Add(string.Format(Properties.Resources.ChatCmdUpkeepMinutes, mins));

                            string timeStr = string.Join(", ", timeParts);

                            var dailyMaterials = FormatUpkeepMaterialsPer24h(dev, secs);
                            var materialsSuffix = string.IsNullOrWhiteSpace(dailyMaterials)
                                ? ""
                                : string.Format(Properties.Resources.ChatCmdUpkeepNeed24h, dailyMaterials);

                            _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcTime, dev.PureName, timeStr) + materialsSuffix);
                        }
                    }
                }
            }
            AppendLog($"[ChatCommand] UpkeepDetail executed by {m.Author}");
            return;
        }

        // Command: Upkeep (Dynamic List)
        var matchedMappings = profile.UpkeepCommandMappings
            .Where(mapping => cmd == mapping.Command.ToLowerInvariant() && mapping.EntityId != 0)
            .ToList();

        if (matchedMappings.Count == 1)
        {
            await ProcessUpkeepCommand(real, matchedMappings[0].EntityId, m.Author);
            return;
        }
        else if (matchedMappings.Count > 1)
        {
            var parts = new List<string>();
            foreach (var mapping in matchedMappings)
            {
                var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == mapping.EntityId && (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor"));
                if (dev != null && (dev.Storage == null || dev.Storage.IsToolCupboard || dev.Storage.ItemsCount == 0))
                {
                    var secs = dev.UpkeepSeconds ?? 0;
                    if (secs <= 0)
                    {
                        parts.Add(string.Format(Properties.Resources.ChatCmdUpkeepEmptyExpiredShort, dev.PureName));
                    }
                    else
                    {
                        int days = secs / 86400;
                        int rem = secs % 86400;
                        int hours = rem / 3600;
                        parts.Add(string.Format(Properties.Resources.ChatCmdUpkeepTimeShort, dev.PureName, days, hours));
                    }
                }
            }
            if (parts.Count > 0)
            {
                _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.ChatCmdUpkeepHeader, string.Join(" | ", parts)));
            }
            else
            {
                _ = SendTeamChatSafeAsync(Properties.Resources.ChatCmdUpkeepNotPaired);
            }
            AppendLog($"[ChatCommand] Multi-Upkeep for cmd={cmd} executed by {m.Author}");
            return;
        }
    }

    private async Task ProcessUpkeepCommand(RustPlusClientReal real, uint entityId, string author)
    {
        var profile = _vm.Selected;
        if (profile == null) return;

        var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == entityId && (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor"));
        if (dev != null && (dev.Storage == null || dev.Storage.IsToolCupboard || dev.Storage.ItemsCount == 0))
        {
            var secs = dev.UpkeepSeconds ?? 0;
            if (secs <= 0)
            {
                _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcEmptyExpired, dev.PureName));
            }
            else
            {
                int days = secs / 86400;
                int rem = secs % 86400;
                int hours = rem / 3600;
                rem = rem % 3600;
                int mins = rem / 60;

                var timeParts = new List<string>();
                if (days > 0) timeParts.Add(string.Format(Properties.Resources.ChatCmdUpkeepDays, days));
                if (hours > 0 || days > 0) timeParts.Add(string.Format(Properties.Resources.ChatCmdUpkeepHours, hours));
                timeParts.Add(string.Format(Properties.Resources.ChatCmdUpkeepMinutes, mins));

                string timeStr = string.Join(", ", timeParts);

                _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcTime, dev.PureName, timeStr));
            }
            AppendLog($"[ChatCommand] Upkeep for {dev.Name} executed by {author}");
        }
        else
        {
            _ = SendTeamChatSafeAsync(Properties.Resources.ChatCmdUpkeepNotPairedSingle);
        }
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
                string stateStr = newState ? Properties.Resources.ChatCmdSwitchStateOn : Properties.Resources.ChatCmdSwitchStateOff;
                _ = SendTeamChatSafeAsync(string.Format(Properties.Resources.ChatCmdSwitchToggled, dev.Name, stateStr));
                AppendLog($"[ChatCommand] {dev.Name} toggled to {newState} by {author}");
            }
            catch (Exception ex)
            {
                AppendLog($"[ChatCommand] Failed to toggle {dev.Name}: {ex.Message}");
            }
        }
        else
        {
            _ = SendTeamChatSafeAsync(Properties.Resources.ChatCmdSwitchNotPaired);
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
            "wood" => Properties.Resources.MaterialWood,
            "stones" => Properties.Resources.MaterialStone,
            "metal.fragments" => Properties.Resources.MaterialMetal,
            "metal.refined" => Properties.Resources.MaterialHQM,
            _ => item.ItemId switch
            {
                -151838493 => Properties.Resources.MaterialWood,
                -2099697608 => Properties.Resources.MaterialStone,
                69511070 => Properties.Resources.MaterialMetal,
                317398316 => Properties.Resources.MaterialHQM,
                _ => MainWindow.ResolveItemName(item.ItemId, item.ShortName)
            }
        };
    }
}
