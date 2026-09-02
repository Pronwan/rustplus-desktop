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
    private void BtnOpenChatCommands_Click(object? sender, System.Windows.RoutedEventArgs? e)
    {
        _vm.Selected?.SyncChatCommands();
        OpenSettingsCategory("chat-commands");
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

    /// <summary>
    /// Answers a command in the channel it was asked in.
    ///
    /// A reply that lands in the wrong channel is worse than no reply: the player who asked sees
    /// nothing, and a room that did not ask gets told. So the channel travels with the command
    /// rather than being read from the UI, which by the time a delayed answer goes out may be
    /// showing something else entirely.
    /// </summary>
    private async Task SendChatCommandResponseAsync(string text, ChatChannel channel = ChatChannel.Team)
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

        // forceChannel, so the alert channel setting cannot redirect an answer: asking !boat in
        // team chat while alerts are pointed at the clan used to answer the clan, leaving the
        // person who asked with nothing and telling ninety people about a boat.
        await SendTeamChatSafeAsync(text, bypassChatAlertMasterBlock: true, forceChannel: channel);
    }

    /// <summary>
    /// Whether a clan-chat message may trigger commands, based on the author's clan role.
    ///
    /// Team chat is unconditional — being in the team is the permission. The clan is not: it can
    /// hold a hundred accounts, so the owner grants command rights per role, and an author whose
    /// role we cannot establish is refused rather than assumed harmless.
    /// </summary>
    private bool IsClanCommandAuthorAllowed(ServerProfile profile, TeamChatMessage m)
    {
        if (!profile.ClanChatCommandsEnabled) return false;

        var member = ClanMembers.FirstOrDefault(c => c.SteamId == m.SteamId);
        if (member == null)
        {
            AppendLog($"[ChatCommand] Clan command from {m.Author} ignored: author not in the last clan pull.");
            return false;
        }

        if (!profile.IsClanRoleAllowed(member.RoleId))
        {
            AppendLog($"[ChatCommand] Clan command from {m.Author} ignored: role '{member.RoleName}' is not permitted.");
            return false;
        }

        return true;
    }

    private async Task ProcessChatCommands(TeamChatMessage m, ChatChannel channel = ChatChannel.Team)
    {
        var profile = _vm.Selected;
        if (profile == null || !profile.ChatCommandsEnabled) return;

        if (channel == ChatChannel.Clan && !IsClanCommandAuthorAllowed(profile, m)) return;

        // Two settings the clan can be denied that the team never is. Both are read once here so
        // every use below reads the same way round, and both default to denied: door codes cannot
        // be unshared, and most clan members have no TC rights on the base a switch belongs to.
        bool allowBaseCodes = channel != ChatChannel.Clan || profile.ClanCommandsAllowBaseCodes;
        bool allowSwitches = channel != ChatChannel.Clan || profile.ClanCommandsAllowSwitches;

        Task Reply(string text) => SendChatCommandResponseAsync(text, channel);

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

        // Chat Master elects one device per *team* to answer, so the team does not get four
        // identical replies. A clan is not a team: its members are spread across teams that know
        // nothing of each other, so there is no election to hold and deferring to a team's winner
        // would leave the clan unanswered by everyone. Whoever switched clan answering on answers;
        // if two people did, they can sort it out between themselves.
        if (channel != ChatChannel.Clan && !CanProcessLocalChatCommands(isPromoteCommand)) return;

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
            // Only advertised once a code exists - listing it on a server with no codes set would
            // send people to a command that answers with nothing.
            if (!string.IsNullOrWhiteSpace(profile.CmdBaseCodes) && profile.HasBaseCodes && allowBaseCodes)
                standardCmds.Add(prefix + profile.CmdBaseCodes);

            string standardMsg = string.Format(Properties.Resources.ChatCmdListHeader, string.Join(", ", standardCmds));
            if (standardMsg.Length > 128) standardMsg = standardMsg.Substring(0, 125) + "...";
            _ = Reply(standardMsg);

            var deviceCmds = new List<string>();
            if (allowSwitches)
            {
                foreach (var mapping in profile.SwitchCommandMappings)
                {
                    if (!string.IsNullOrWhiteSpace(mapping.Command) && mapping.EntityId != 0)
                    {
                        var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == mapping.EntityId && d.Kind == "SmartSwitch");
                        if (dev != null) deviceCmds.Add($"[{dev.PureName}]: {prefix}{mapping.Command}");
                    }
                }
            }
            // Logic rules ride along with the switch permission: a rule triggered from chat is
            // there to flip devices, so advertising it to a clan that may not touch switches
            // would just be the same command under a different name.
            if (allowSwitches && profile.IsLogicEngineActive && !_chatFeaturesBlockedByMaster && profile.LogicRules != null)
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
                    await Reply(devMsg);
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
            _ = Reply(msg);
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
            _ = Reply(msg.Trim());
            AppendLog($"[ChatCommand] Time executed by {m.Author}");
            return;
        }

        // Command: AFK
        if (!string.IsNullOrWhiteSpace(profile.CmdAfk) && cmd == profile.CmdAfk.ToLowerInvariant())
        {
            // AFK is only knowable for the team: the game reports movement for team members, and
            // nothing at all for the rest of the clan. Answering a clan that asks would otherwise
            // read as "these four are AFK, the other ninety are not", so the answer says whose
            // status it is actually reporting.
            string afkScope = channel == ChatChannel.Clan
                ? (RustPlusDesk.Helpers.Loc.TextOrNull("ChatCmdAfkTeamScope") ?? "Team")
                : "";
            string afkPrefix = string.IsNullOrEmpty(afkScope) ? "AFK: " : $"AFK ({afkScope}): ";

            var afkMembers = TeamMembers.Where(t => t.IsAfk).ToList();
            if (afkMembers.Count == 0)
            {
                var noOneAfkMsg = RustPlusDesk.Helpers.Loc.TextOrNull("ChatCmdNoOneAfk") ?? "No one is AFK.";
                if (!string.IsNullOrEmpty(afkScope)) noOneAfkMsg = $"({afkScope}) {noOneAfkMsg}";
                _ = Reply(noOneAfkMsg);
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
                _ = Reply(afkPrefix + string.Join(" | ", parts));
            }
            AppendLog($"[ChatCommand] AFK executed by {m.Author}");
            return;
        }

        // Command: Promote
        if (cmd == profile.CmdPromote.ToLowerInvariant())
        {
            _ = real.PromoteToLeaderAsync(m.SteamId);
            _ = Reply(string.Format(Properties.Resources.ChatCmdPromoteResponse, m.Author));
            AppendLog($"[ChatCommand] Promote executed by {m.Author}");
            return;
        }

        // Command: Deep Sea
        if (cmd == profile.CmdDeepSea.ToLowerInvariant())
        {
            // On a server without event markers the local Deep Sea state is never populated —
            // it was fed by the shop poll. Answer from the shared audio detections instead.
            if (Services.EventCapabilities.IsCloudSourced)
            {
                _ = Reply(BuildCloudDeepSeaAnswer());
                AppendLog($"[ChatCommand] DeepSea (audio) executed by {m.Author}");
                return;
            }

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
            _ = Reply(msg);
            AppendLog($"[ChatCommand] DeepSea executed by {m.Author}");
            return;
        }

        // Command: Cargo
        if (cmd == profile.CmdCargo.ToLowerInvariant())
        {
            // Docking, harbour and departure all need the ship's position. Audio only ever
            // tells us that a cargo spawned, so the fallback answer says exactly that.
            if (Services.EventCapabilities.IsCloudSourced)
            {
                _ = Reply(BuildCloudCargoAnswer());
                AppendLog($"[ChatCommand] Cargo (audio) executed by {m.Author}");
                return;
            }

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
            _ = Reply(msg);
            AppendLog($"[ChatCommand] Cargo executed by {m.Author}");
            return;
        }

        // Command: Oil Rig
        if (cmd == profile.CmdOilRig.ToLowerInvariant())
        {
            // The audio cue cannot say which rig it was, and there is no unlock countdown
            // without the API. Report when crates were last heard and nothing more.
            if (Services.EventCapabilities.IsCloudSourced)
            {
                _ = Reply(BuildCloudOilRigAnswer());
                AppendLog($"[ChatCommand] OilRig (audio) executed by {m.Author}");
                return;
            }

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
            _ = Reply(string.Join(" | ", parts));
            AppendLog($"[ChatCommand] OilRig executed by {m.Author}");
            return;
        }

        // Command: Patrol Heli
        if (cmd == profile.CmdHeli.ToLowerInvariant())
        {
            string msg;
            var heliMarker = _dynStates.Values.FirstOrDefault(s => s.Type == 8);
            bool isHeliActive = heliMarker != null;
            if (heliMarker != null)
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
            _ = Reply(msg);
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
            _ = Reply(msg);
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
                    _ = Reply($"{timer.Name}: {timeStr}");
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
                _ = Reply("No active timers.");
            }
            else
            {
                var timerStrings = profile.CustomTimers.Select(t => $"{profile.ChatCommandPrefix}{t.Command} : {t.RemainingTimeText}").ToList();
                string output = string.Join(" | ", timerStrings);
                _ = Reply(output);
            }
            return;
        }
        else if (cmd.StartsWith(createTimerCmd + " "))
        {
            if (profile.CustomTimers.Count >= 5)
            {
                _ = Reply(Properties.Resources.ChatCmdTimerMaxReached ?? "Maximum of 5 timers allowed.");
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
                        // Warnings go back to whoever asked for the timer, not to wherever alerts
                        // happen to be pointed.
                        OriginChannel = channel.ToString(),
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
                        _ = Reply(msg);
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
        if (allowSwitches && profile.IsLogicEngineActive && !_chatFeaturesBlockedByMaster && profile.LogicRules != null)
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
        // Left unrecognised rather than answered with a refusal when the clan may not use them:
        // "you are not allowed to use !lights" would confirm to the whole clan which switches
        // exist and what they are called, which is most of what an attacker wanted to know.
        var matchedSwitches = (allowSwitches
                ? profile.SwitchCommandMappings.AsEnumerable()
                : Enumerable.Empty<ChatCommandMapping>())
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
                            _ = Reply(string.Format(Properties.Resources.ChatCmdSwitchToggled, toggledNames[0], stateStr));
                        }
                        else
                        {
                            string names = string.Join(", ", toggledNames);
                            if (names.Length > 80) names = names.Substring(0, 77) + "...";
                            _ = Reply(string.Format(Properties.Resources.ChatCmdSwitchToggled, names, stateStr));
                        }
                        AppendLog($"[ChatCommand] Toggled {toggledNames.Count} switches to {targetOn} by {m.Author}");
                    }
                }
                else
                {
                    _ = Reply(Properties.Resources.ChatCmdSwitchNotPaired);
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
                _ = Reply(Properties.Resources.ChatCmdUpkeepNoTcMapped);
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
                            _ = Reply(string.Format(Properties.Resources.ChatCmdUpkeepTcEmptyExpired, dev.PureName));
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

                            _ = Reply(string.Format(Properties.Resources.ChatCmdUpkeepTcTime, dev.PureName, timeStr) + materialsSuffix);
                        }
                    }
                }
            }
            AppendLog($"[ChatCommand] UpkeepDetail executed by {m.Author}");
            return;
        }

        // Command: Base Codes
        if (allowBaseCodes && !string.IsNullOrWhiteSpace(profile.CmdBaseCodes) && cmd == profile.CmdBaseCodes.ToLowerInvariant())
        {
            // Half-typed rows are skipped rather than shown: a three-digit code in team chat is
            // worse than no answer, because someone will try it on a door.
            var fallbackName = Properties.Resources.GetString("BaseCodesNameDefault") ?? "Doorcode";
            var parts = profile.BaseCodes
                .Where(c => c.IsComplete)
                .Select(c => $"{(string.IsNullOrWhiteSpace(c.Label) ? fallbackName : c.Label.Trim())}: {c.Code}")
                .ToList();

            if (parts.Count == 0)
            {
                _ = Reply(Properties.Resources.ChatCmdNoBaseCodes);
            }
            else
            {
                string msg = string.Join(" | ", parts);
                // Team chat truncates at 128 characters. Five labelled codes fit comfortably, but a
                // long custom name could push it over, so drop whole entries instead of letting the
                // server cut a code in half.
                while (msg.Length > 128 && parts.Count > 1)
                {
                    parts.RemoveAt(parts.Count - 1);
                    msg = string.Join(" | ", parts) + " ...";
                }
                _ = Reply(msg);
            }

            AppendLog($"[ChatCommand] BaseCodes executed by {m.Author}");
            return;
        }

        // Command: Upkeep (Dynamic List)
        var matchedMappings = profile.UpkeepCommandMappings
            .Where(mapping => cmd == mapping.Command?.ToLowerInvariant() && mapping.EntityId != 0)
            .ToList();

        if (matchedMappings.Count == 1)
        {
            await ProcessUpkeepCommand(real, matchedMappings[0].EntityId, m.Author, channel);
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
                _ = Reply(string.Format(Properties.Resources.ChatCmdUpkeepHeader, string.Join(" | ", parts)));
            }
            else
            {
                _ = Reply(Properties.Resources.ChatCmdUpkeepNotPaired);
            }
            AppendLog($"[ChatCommand] Multi-Upkeep for cmd={cmd} executed by {m.Author}");
            return;
        }
    }

    private async Task ProcessUpkeepCommand(RustPlusClientReal real, uint entityId, string author, ChatChannel channel = ChatChannel.Team)
    {
        var profile = _vm.Selected;
        if (profile == null) return;

        var dev = profile.AllDevices.FirstOrDefault(d => d.EntityId == entityId && (d.Kind == "StorageMonitor" || d.Kind == "Storage Monitor"));
        if (dev != null && (dev.Storage == null || dev.Storage.IsToolCupboard || dev.Storage.ItemsCount == 0))
        {
            var secs = dev.UpkeepSeconds ?? 0;
            if (secs <= 0)
            {
                _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcEmptyExpired, dev.PureName), channel);
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

                _ = SendChatCommandResponseAsync(string.Format(Properties.Resources.ChatCmdUpkeepTcTime, dev.PureName, timeStr), channel);
            }
            AppendLog($"[ChatCommand] Upkeep for {dev.Name} executed by {author}");
        }
        else
        {
            _ = SendChatCommandResponseAsync(Properties.Resources.ChatCmdUpkeepNotPairedSingle, channel);
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
        // Same answer the in-game command gives — the Discord bot must not report a different
        // state than team chat for the same server.
        if (Services.EventCapabilities.IsCloudSourced) return BuildCloudDeepSeaAnswer();

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
        if (Services.EventCapabilities.IsCloudSourced) return BuildCloudCargoAnswer();

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
        if (Services.EventCapabilities.IsCloudSourced) return BuildCloudOilRigAnswer();

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
        // No server-wide audio cue exists for the Patrol Heli, so on a fallback server the
        // honest answer is that it cannot be tracked — not a stale "not active".
        if (Services.EventCapabilities.IsCloudSourced)
            return Properties.Resources.EventNotTrackableOnServer;

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
        if (Services.EventCapabilities.IsCloudSourced)
            return Properties.Resources.EventNotTrackableOnServer;

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
