using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System.Threading;


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

    private void ChatCommandsOverlay_CommandsEnabledChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            try { _vm.Save(); } catch { }
            RequestTeamFeatureMasterSync();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private DateTime _lastChatCommandTime = DateTime.MinValue;
    private const int ChatCommandCooldownSeconds = 2; // 2s cooldown for system stability

    private async Task SendChatCommandResponseAsync(string text)
    {
        var profile = _vm.Selected;
        if (profile != null)
        {
            int delayMs = (int)(profile.ChatResponseDelaySeconds * 1000);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }
        }
        await SendTeamChatSafeAsync(text, bypassChatAlertMasterBlock: true);
    }

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
        var isPromoteCommand = !string.IsNullOrWhiteSpace(profile.CmdPromote)
            && cmd == profile.CmdPromote.ToLowerInvariant();
        if (!CanProcessLocalChatCommands(isPromoteCommand)) return;

        _lastChatCommandTime = DateTime.UtcNow;

        if (_rust is not RustPlusClientReal real) return;

        // Command: List Commands
        if (cmd == profile.CmdList.ToLowerInvariant())
        {
            var standardCmds = new List<string>();
            var firstTimer = profile.CustomTimers.FirstOrDefault();
            if (firstTimer != null) standardCmds.Add(prefix + firstTimer.Command);
            
            if (!string.IsNullOrWhiteSpace(profile.CmdPop)) standardCmds.Add(prefix + profile.CmdPop);
            if (!string.IsNullOrWhiteSpace(profile.CmdTime)) standardCmds.Add(prefix + profile.CmdTime);
            if (!string.IsNullOrWhiteSpace(profile.CmdPromote)) standardCmds.Add(prefix + profile.CmdPromote);
            if (!string.IsNullOrWhiteSpace(profile.CmdDeepSea)) standardCmds.Add(prefix + profile.CmdDeepSea);
            if (!string.IsNullOrWhiteSpace(profile.CmdCargo)) standardCmds.Add(prefix + profile.CmdCargo);
            if (!string.IsNullOrWhiteSpace(profile.CmdOilRig)) standardCmds.Add(prefix + profile.CmdOilRig);
            if (!string.IsNullOrWhiteSpace(profile.CmdHeli)) standardCmds.Add(prefix + profile.CmdHeli);
            if (!string.IsNullOrWhiteSpace(profile.CmdVendor)) standardCmds.Add(prefix + profile.CmdVendor);
            if (!string.IsNullOrWhiteSpace(profile.CmdUpkeepDetail)) standardCmds.Add(prefix + profile.CmdUpkeepDetail);
            if (!string.IsNullOrWhiteSpace(profile.CmdAfk)) standardCmds.Add(prefix + profile.CmdAfk);

            string standardMsg = string.Format(Properties.Resources.ChatCmdListHeader, string.Join(", ", standardCmds));
            if (standardMsg.Length > 128) standardMsg = standardMsg.Substring(0, 125) + "...";
            _ = SendChatCommandResponseAsync(standardMsg);

            var deviceCmds = new List<string>();
            foreach (var mapping in profile.SwitchCommandMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.Command) && mapping.EntityId != 0)
                {
                    var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == mapping.EntityId && d.Kind == "SmartSwitch");
                    if (dev != null) deviceCmds.Add($"[{dev.PureName}]: {prefix}{mapping.Command}");
                }
            }
            if (profile.IsLogicEngineActive && !_chatFeaturesBlockedByMaster && profile.LogicRules != null)
            {
                foreach (var rule in profile.LogicRules)
                {
                    if (rule.IsEnabled && rule.TriggerType == "ChatCommand" && !string.IsNullOrWhiteSpace(rule.TriggerCommand))
                    {
                        string cleanCmd = rule.TriggerCommand.Trim();
                        if (cleanCmd.StartsWith(prefix)) cleanCmd = cleanCmd.Substring(prefix.Length).Trim();
                        deviceCmds.Add($"[Rule: {rule.Name}]: {prefix}{cleanCmd}");
                    }
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
                    await SendChatCommandResponseAsync(devMsg);
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
            _ = SendChatCommandResponseAsync(msg);
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
            _ = SendChatCommandResponseAsync(msg.Trim());
            AppendLog($"[ChatCommand] Time executed by {m.Author}");
            return;
        }

        // Command: AFK
        if (!string.IsNullOrWhiteSpace(profile.CmdAfk) && cmd == profile.CmdAfk.ToLowerInvariant())
        {
            var afkMembers = TeamMembers.Where(t => t.IsAfk).ToList();
            if (afkMembers.Count == 0)
            {
                var noOneAfkMsg = Properties.Resources.ResourceManager.GetString("ChatCmdNoOneAfk") ?? "No one is AFK.";
                _ = SendChatCommandResponseAsync(noOneAfkMsg);
            }
            else
            {
                var now = DateTime.UtcNow;
                var parts = afkMembers.Select(t => 
                {
                    var elapsed = now - t.LastMoveTime;
                    int totalSecs = (int)elapsed.TotalSeconds;
                    int mins = totalSecs / 60;
                    int secs = totalSecs % 60;
                    return $"{t.Name} - {mins}:{secs:D2}";
                }).ToList();
                _ = SendChatCommandResponseAsync("AFK: " + string.Join(" | ", parts));
            }
            AppendLog($"[ChatCommand] AFK executed by {m.Author}");
            return;
        }

        // Command: Promote
        if (cmd == profile.CmdPromote.ToLowerInvariant())
        {
            _ = real.PromoteToLeaderAsync(m.SteamId);
            _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdPromoteResponse, m.Author));
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
            _ = SendChatCommandResponseAsync(msg);
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
            _ = SendChatCommandResponseAsync(msg);
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
            _ = SendChatCommandResponseAsync(string.Join(" | ", parts));
            AppendLog($"[ChatCommand] OilRig executed by {m.Author}");
            return;
        }

        // Command: Patrol Heli
        if (cmd == profile.CmdHeli.ToLowerInvariant())
        {
            string msg;
            var heliMarker = _dynStates.Values.FirstOrDefault(s => s.Type == 8);
            bool isHeliActive = heliMarker != null;
            if (isHeliActive)
            {
                string grid = GetGridLabel(heliMarker.LastRealX, heliMarker.LastRealY);
                if (_heliSpawnTime.HasValue)
                {
                    var elapsed = DateTime.UtcNow - _heliSpawnTime.Value;
                    msg = string.Format(Properties.Resources.ChatCmdHeliActive, FormatAgo(elapsed)) + $" [{grid}]";
                }
                else
                {
                    msg = Properties.Resources.ChatCmdHeliActiveMidEvent + $" [{grid}]";
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
            _ = SendChatCommandResponseAsync(msg);
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
            _ = SendChatCommandResponseAsync(msg);
            AppendLog($"[ChatCommand] Vendor executed by {m.Author}");
            return;
        }

        // Check Custom Timers Check Commands
        foreach (var timer in profile.CustomTimers)
        {
            if (cmd == timer.Command.ToLowerInvariant())
            {
                var remaining = timer.EndTimeUtc - DateTime.UtcNow;
                if (remaining.TotalSeconds > 0)
                {
                    string timeStr = remaining.TotalHours >= 1.0 
                        ? $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
                        : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                    _ = SendChatCommandResponseAsync($"{timer.Name}: {timeStr}");
                    AppendLog($"[ChatCommand] Timer '{timer.Name}' checked by {m.Author}");
                }
                return;
            }
        }

        // Create Custom Timer (e.g. !timer TEST,70) or List Timers (!timer)
        var createTimerCmd = profile.CmdCustomTimer.ToLowerInvariant();
        if (cmd == createTimerCmd)
        {
            if (profile.CustomTimers.Count == 0)
            {
                _ = SendChatCommandResponseAsync("No active timers.");
            }
            else
            {
                var timerStrings = profile.CustomTimers.Select(t => $"{profile.ChatCommandPrefix}{t.Command} : {t.RemainingTimeText}").ToList();
                string output = string.Join(" | ", timerStrings);
                _ = SendChatCommandResponseAsync(output);
            }
            return;
        }
        else if (cmd.StartsWith(createTimerCmd + " "))
        {
            if (profile.CustomTimers.Count >= 5)
            {
                _ = SendChatCommandResponseAsync(Properties.Resources.ChatCmdTimerMaxReached ?? "Maximum of 5 timers allowed.");
                return;
            }

            var args = cmd.Substring(createTimerCmd.Length + 1).Split(',');
            if (args.Length == 2)
            {
                string name = args[0].Trim();
                string timePart = args[1].Trim();

                int hours = 0, mins = 0, secs = 0;

                if (timePart.Contains(':'))
                {
                    var parts = timePart.Split(':');
                    if (parts.Length == 3)
                    {
                        int.TryParse(parts[0], out hours);
                        int.TryParse(parts[1], out mins);
                        int.TryParse(parts[2], out secs);
                    }
                    else if (parts.Length == 2)
                    {
                        int.TryParse(parts[0], out mins);
                        int.TryParse(parts[1], out secs);
                    }
                }
                else
                {
                    int.TryParse(timePart, out mins);
                }

                int totalSecs = hours * 3600 + mins * 60 + secs;
                if (totalSecs <= 0) return;

                if (string.IsNullOrWhiteSpace(name) || !char.IsLetter(name[0]))
                {
                    _ = SendTeamChatSafeAsync(Properties.Resources.TimerNameMustStartWithLetter);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var newCmd = new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLower();
                    double totalMins = totalSecs / 60.0;
                    var timer = new CustomTimer
                    {
                        Name = name,
                        Command = newCmd,
                        EndTimeUtc = DateTime.UtcNow.AddSeconds(totalSecs),
                        CreatedNotified = false,
                        Notified60 = totalMins <= 60,
                        Notified30 = totalMins <= 30,
                        Notified10 = totalMins <= 10,
                        Notified3 = totalMins <= 3
                    };
                    Dispatcher.Invoke(() => profile.CustomTimers.Add(timer));

                    if (profile.AlertCustomTimer)
                    {
                        var msg = string.Format(Properties.Resources.TimerCreated, profile.ChatCommandPrefix + newCmd, hours, mins, secs);
                        _ = SendChatCommandResponseAsync(msg);
                    }
                    AppendLog($"[ChatCommand] Timer created by {m.Author}: {name} for {hours}h {mins}m {secs}s");
                }
            }
            return;
        }

        // Check if logic engine is running an action
        if (_logicEngineRunningAction)
        {
            AppendLog("[ChatCommand] Switch command ignored: Logic Engine is currently executing an action.");
            return;
        }

        // Check Logic Engine rules
        if (profile.IsLogicEngineActive && !_chatFeaturesBlockedByMaster && profile.LogicRules != null)
        {
            var matchedRule = profile.LogicRules.FirstOrDefault(r => {
                if (!r.IsEnabled || r.TriggerType != "ChatCommand") return false;
                string cleanCmd = r.TriggerCommand?.Trim().ToLowerInvariant() ?? "";
                if (cleanCmd.StartsWith(prefix)) cleanCmd = cleanCmd.Substring(prefix.Length).Trim();
                return cleanCmd == cmd;
            });
            if (matchedRule != null)
            {
                TriggerLogicEngineOnChatCommand(cmd);
                return;
            }
        }

        // Command: Switches (Dynamic List)
        var matchedSwitches = profile.SwitchCommandMappings
            .Where(mapping => cmd == mapping.Command?.ToLowerInvariant() && mapping.EntityId != 0)
            .ToList();

        if (matchedSwitches.Count > 0)
        {
            var devsToToggle = matchedSwitches
                .Select(m => profile.AllDevices.FirstOrDefault(d => d.EntityId == m.EntityId && (d.Kind == "SmartSwitch" || d.IsGroup)))
                .Where(d => d != null)
                .ToList();

            if (devsToToggle.Count > 0)
            {
                var finalSwitches = new List<SmartDevice>();
                void AddSwitches(SmartDevice d)
                {
                    if (d.IsGroup && d.Children != null)
                    {
                        foreach (var c in d.Children) AddSwitches(c);
                    }
                    else if (d.Kind == "SmartSwitch" || d.Kind == "Smart Switch")
                    {
                        finalSwitches.Add(d);
                    }
                }
                foreach (var dev in devsToToggle) AddSwitches(dev!);

                finalSwitches = finalSwitches.Distinct().ToList();

                if (finalSwitches.Count > 0)
                {
                    bool targetOn = !(finalSwitches.First().IsOn ?? false);
                    var toggledNames = new List<string>();

                    foreach (var dev in finalSwitches)
                    {
                        if (dev.IsOn == targetOn) continue;

                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            await real.ToggleSmartSwitchAsync(dev.EntityId, targetOn, cts.Token);
                            toggledNames.Add(dev.PureName ?? dev.EntityId.ToString());
                            dev.IsOn = targetOn;
                            await Task.Delay(800);
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[ChatCommand] Failed to toggle {dev.PureName}: {ex.Message}");
                        }
                    }

                    if (toggledNames.Count > 0)
                    {
                        string stateStr = targetOn ? Properties.Resources.ChatCmdSwitchStateOn : Properties.Resources.ChatCmdSwitchStateOff;
                        if (toggledNames.Count == 1)
                        {
                            _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdSwitchToggled, toggledNames[0], stateStr));
                        }
                        else
                        {
                            string names = string.Join(", ", toggledNames);
                            if (names.Length > 80) names = names.Substring(0, 77) + "...";
                            _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdSwitchToggled, names, stateStr));
                        }
                        AppendLog($"[ChatCommand] Toggled {toggledNames.Count} switches to {targetOn} by {m.Author}");
                    }
                }
                else
                {
                    _ = SendChatCommandResponseAsync(Properties.Resources.ChatCmdSwitchNotPaired);
                }
            }
            return;
        }

        // Command: Detailed Upkeep (Global)
        if (cmd == profile.CmdUpkeepDetail.ToLowerInvariant())
        {
            var tcs = profile.UpkeepCommandMappings.Where(mapping => mapping.EntityId != 0).ToList();
            if (tcs.Count == 0)
            {
                _ = SendChatCommandResponseAsync(Properties.Resources.ChatCmdUpkeepNoTcMapped);
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
                            int delayMs = (int)(Math.Max(2.0, profile.ChatResponseDelaySeconds) * 1000);
                            await Task.Delay(delayMs);
                        }
                        first = false;

                        var secs = dev.UpkeepSeconds ?? 0;
                        if (secs <= 0)
                        {
                            _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcEmptyExpired, dev.PureName));
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

                            _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcTime, dev.PureName, timeStr) + materialsSuffix);
                        }
                    }
                }
            }
            AppendLog($"[ChatCommand] UpkeepDetail executed by {m.Author}");
            return;
        }

        // Command: Upkeep (Dynamic List)
        var matchedMappings = profile.UpkeepCommandMappings
            .Where(mapping => cmd == mapping.Command?.ToLowerInvariant() && mapping.EntityId != 0)
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
                _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdUpkeepHeader, string.Join(" | ", parts)));
            }
            else
            {
                _ = SendChatCommandResponseAsync(Properties.Resources.ChatCmdUpkeepNotPaired);
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
                _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcEmptyExpired, dev.PureName));
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

                _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcTime, dev.PureName, timeStr));
            }
            AppendLog($"[ChatCommand] Upkeep for {dev.Name} executed by {author}");
        }
        else
        {
            _ = SendChatCommandResponseAsync(Properties.Resources.ChatCmdUpkeepNotPairedSingle);
        }
    }

    public async Task<bool> ToggleSmartSwitchFromDiscordAsync(uint entityId, bool state)
    {
        if (_rust == null) return false;
        try
        {
            await _rust.ToggleSmartSwitchAsync(entityId, state);
            return true;
        }
        catch (Exception ex)
        {
            AppendLog($"[DiscordBotListener] Failed to toggle switch {entityId}: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool success, string message)> ToggleSmartSwitchFromDiscordAsync(string nameOrId)
    {
        if (_rust == null || _vm?.Selected == null) return (false, "Not connected to server.");
        try
        {
            SmartDevice? dev = null;
            if (uint.TryParse(nameOrId, out var id))
            {
                dev = _vm.Selected.AllDevices.FirstOrDefault(d =>
                    (d.Kind ?? "").Equals("SmartSwitch", StringComparison.OrdinalIgnoreCase) &&
                    d.EntityId == id);
            }
            else
            {
                // Search by name or alias (case-insensitive, partial match)
                dev = _vm.Selected.AllDevices.FirstOrDefault(d =>
                    (d.Kind ?? "").Equals("SmartSwitch", StringComparison.OrdinalIgnoreCase) &&
                    ((d.Alias ?? d.Name ?? "").Contains(nameOrId, StringComparison.OrdinalIgnoreCase) ||
                     (d.Name ?? "").Contains(nameOrId, StringComparison.OrdinalIgnoreCase)));
            }

            if (dev == null)
            {
                AppendLog($"[DiscordBotListener] No smart switch found matching: {nameOrId}");
                return (false, $"❌ No switch found matching '{nameOrId}'. Use /devicelist to see available devices.");
            }

            // Toggle: invert current state
            bool newState = !(dev.IsOn ?? false);
            await _rust.ToggleSmartSwitchAsync(dev.EntityId, newState);
            string label = dev.Alias ?? dev.Name ?? dev.EntityId.ToString();
            AppendLog($"[DiscordBotListener] Toggled {label} → {(newState ? "ON" : "OFF")}");
            return (true, $"{(newState ? "✅" : "⛔")} **{label}** turned **{(newState ? "ON" : "OFF")}**");
        }
        catch (Exception ex)
        {
            AppendLog($"[DiscordBotListener] Failed to toggle switch {nameOrId}: {ex.Message}");
            return (false, $"❌ Error toggling switch: {ex.Message}");
        }
    }

    public string GetSmartSwitchListForDiscord()
    {
        if (_vm?.Selected == null) return "❌ Not connected to server.";

        var switches = _vm.Selected.AllDevices
            .Where(d => (d.Kind ?? "").Equals("SmartSwitch", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (switches.Count == 0) return "📋 No smart switches paired.";

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("📋 **Paired Smart Switches:**");
        foreach (var sw in switches)
        {
            string label = sw.Alias ?? sw.Name ?? sw.EntityId.ToString();
            string state = sw.IsMissing ? "❌ OFFLINE" : (sw.IsOn == true ? "🟢 ON" : "🔴 OFF");
            lines.AppendLine($"• **{label}** `#{sw.EntityId}` — {state}");
        }
        return lines.ToString().TrimEnd();
    }

    public string GetDeepSeaStatusForDiscord()
    {
        if (_deepSeaActive)
        {
            if (_deepSeaSpawnTime.HasValue)
            {
                var elapsed = DateTime.UtcNow - _deepSeaSpawnTime.Value;
                return $"🌊 **Deep Sea** is active (spawned {FormatAgo(elapsed)} ago)";
            }
            return "🌊 **Deep Sea** is currently active (spawn time unknown – connected mid-event)";
        }
        if (_deepSeaDespawnTime.HasValue)
        {
            var ago = DateTime.UtcNow - _deepSeaDespawnTime.Value;
            return $"🌊 **Deep Sea** ended {(int)ago.TotalMinutes} minute(s) ago";
        }
        return "🌊 **Deep Sea**: No data yet – event may not have occurred this wipe";
    }

    public string GetCargoStatusForDiscord()
    {
        var activeCargo = _cargoDockStates.Values.FirstOrDefault();
        if (activeCargo != null)
        {
            string harborName = activeCargo.HarborName ?? "harbor";
            if (activeCargo.IsDocked && activeCargo.DockTime.HasValue)
            {
                int dockDuration = TrackingService.GetLearnedDockingDuration(_vm?.Selected?.Host ?? "");
                if (dockDuration > 0 && !activeCargo.WasAlreadyDocked)
                {
                    var dockRemain = TimeSpan.FromMinutes(dockDuration) - (DateTime.UtcNow - activeCargo.DockTime.Value);
                    if (dockRemain.TotalMinutes > 0)
                        return $"🚢 **Cargo Ship** is docked at {harborName} – departs in ~{(int)dockRemain.TotalMinutes}m";
                    return $"🚢 **Cargo Ship** is docked at {harborName} – preparing to depart";
                }
                return $"🚢 **Cargo Ship** is docked at {harborName}";
            }
            if (activeCargo.SeenAtEdge)
            {
                int fullLife = TrackingService.GetLearnedCargoFullLife(_vm?.Selected?.Host ?? "");
                if (fullLife > 0 && activeCargo.FirstSeen.HasValue)
                {
                    var remain = TimeSpan.FromMinutes(fullLife) - (DateTime.UtcNow - activeCargo.FirstSeen.Value);
                    if (remain.TotalMinutes > 0)
                        return $"🚢 **Cargo Ship** is active – leaves in ~{(int)remain.TotalMinutes}m";
                    return "🚢 **Cargo Ship** is active – preparing to leave";
                }
                return "🚢 **Cargo Ship** is active (total duration not yet learned)";
            }
            return "🚢 **Cargo Ship** is active (connected mid-route – time unknown)";
        }
        if (_cargoLastDespawnUtc.HasValue)
        {
            var ago = DateTime.UtcNow - _cargoLastDespawnUtc.Value;
            return $"🚢 **Cargo Ship** left {(int)ago.TotalMinutes} minute(s) ago";
        }
        return "🚢 **Cargo Ship**: Not currently on the map";
    }

    public string GetOilRigStatusForDiscord()
    {
        var parts = new List<string>();
        foreach (var rigName in new[] { "Small Oil Rig", "Large Oil Rig" })
        {
            string emoji = rigName.Contains("Small") ? "🛢️" : "🏭";
            var timeLeft = _monumentWatcher.GetActiveEventTimeLeft(rigName);
            if (timeLeft.HasValue)
            {
                parts.Add($"{emoji} **{rigName}**: Locked crate in {(int)timeLeft.Value.TotalMinutes}m {timeLeft.Value.Seconds}s");
            }
            else
            {
                var lastTrig = _monumentWatcher.GetLastTriggered(rigName);
                if (lastTrig.HasValue)
                {
                    var ago = DateTime.UtcNow - lastTrig.Value;
                    parts.Add($"{emoji} **{rigName}**: Last called {(int)ago.TotalMinutes}m ago");
                }
                else
                {
                    parts.Add($"{emoji} **{rigName}**: Not called this session");
                }
            }
        }
        return string.Join("\n", parts);
    }

    public string GetHeliStatusForDiscord()
    {
        bool isHeliActive = _dynStates.Values.Any(s => s.Type == 8);
        if (isHeliActive)
        {
            if (_heliSpawnTime.HasValue)
            {
                var elapsed = DateTime.UtcNow - _heliSpawnTime.Value;
                return $"🚁 **Patrol Heli** is active (spawned {FormatAgo(elapsed)} ago)";
            }
            return "🚁 **Patrol Heli** is active (spawn time unknown – connected mid-event)";
        }
        if (_heliLastEventUtc.HasValue)
        {
            var ago = DateTime.UtcNow - _heliLastEventUtc.Value;
            string reason = _heliLastEventWasCrash ? "was shot down" : "left the map";
            return $"🚁 **Patrol Heli** {reason} {FormatAgo(ago)} ago";
        }
        return "🚁 **Patrol Heli**: No sighting this session";
    }

    public string GetVendorStatusForDiscord()
    {
        bool isVendorActive = _dynStates.Values.Any(s => s.Type == 6);
        if (isVendorActive)
        {
            if (_vendorSpawnTime.HasValue)
            {
                var elapsed = DateTime.UtcNow - _vendorSpawnTime.Value;
                return $"🛒 **Travelling Vendor** is active (spawned {FormatAgo(elapsed)} ago)";
            }
            return "🛒 **Travelling Vendor** is active (spawn time unknown – connected mid-event)";
        }
        if (_vendorDespawnTime.HasValue)
        {
            var ago = DateTime.UtcNow - _vendorDespawnTime.Value;
            return $"🛒 **Travelling Vendor** left {FormatAgo(ago)} ago";
        }
        return "🛒 **Travelling Vendor**: No sighting this session";
    }

    public string GetUpkeepDetailsForDiscord()
    {
        var profile = _vm?.Selected;
        if (profile == null) return "❌ Not connected to server.";

        var tcs = profile.AllDevices
            .Where(d => (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor") && d.Storage?.IsToolCupboard == true)
            .ToList();

        if (tcs.Count == 0) return "🏠 No Tool Cupboards monitored.";

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("🏠 **Upkeep Status:**");
        foreach (var tc in tcs)
        {
            string label = tc.Alias ?? tc.Name ?? $"TC #{tc.EntityId}";
            string upkeep = tc.StorageSummary;
            string state = tc.IsMissing ? "❌ OFFLINE" : upkeep;
            lines.AppendLine($"• **{label}**: {state}");
        }
        return lines.ToString().TrimEnd();
    }

    public string GetDiscordCommandListForDiscord()
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("📋 **Available Bot Commands:**");
        lines.AppendLine("• `/time` – Current server time");
        lines.AppendLine("• `/pop` – Player count & queue");
        lines.AppendLine("• `/heli` – Patrol Heli status");
        lines.AppendLine("• `/cargo` – Cargo Ship status");
        lines.AppendLine("• `/oilrig` – Oil Rig status");
        lines.AppendLine("• `/deepsea` – Deep Sea event status");
        lines.AppendLine("• `/vendor` – Travelling Vendor status");
        lines.AppendLine("• `/upkeep` – Tool Cupboard upkeep details");
        lines.AppendLine("• `/switch device:<name>` – Toggle a smart switch");
        lines.AppendLine("• `/devicelist` – List all paired smart switches");
        lines.AppendLine("• `/commands` – Show this list");
        return lines.ToString().TrimEnd();
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
