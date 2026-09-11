using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

/// <summary>
/// The main window's side of the mini-map command dock: it owns the data, the dock only draws.
/// Every member here is a read of state that already exists somewhere else in the window — the
/// dock is a second view of the map page, not a second source of truth.
/// </summary>
public partial class MainWindow : ICommandDockHost
{
    /// <summary>
    /// The event dock's last built list. Captured where it is rendered so the tiles show
    /// exactly what the dock on the map shows, including the crowd-sourced fallback.
    /// </summary>
    private List<EventDockItem> _lastEventDockItems = new();

    /// <summary>
    /// Hands the session tracker one reading a second. Called once during start-up; the tracker
    /// owns the timer, because most of what it counts is elapsed time and it needs one anyway.
    /// </summary>
    internal void StartSessionTracking()
    {
        SessionTracker.Instance.Source = () =>
        {
            var key = DockServerKey;
            if (key == null || _vm?.Selected?.IsFullConnected != true) return null;

            var me = TeamMembers.FirstOrDefault(t => t.SteamId == _mySteamId);

            return new SessionSnapshot(
                Connected: true,
                ServerKey: key,
                MySteamId: _mySteamId,
                MyX: me?.X,
                MyY: me?.Y,
                MyAfk: me?.IsAfk == true,
                Population: ParsePopulation(_vm?.ServerPlayers),
                Team: TeamMembers.Select(t => (t.SteamId, t.IsDead)).ToList());
        };
    }

    /// <summary>"128/200" as 128. The HUD keeps it as text, which is all the HUD needs.</summary>
    private static int ParsePopulation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var head = text.Split('/')[0].Trim();
        return int.TryParse(head, out var players) ? players : 0;
    }

    public string? DockServerKey
    {
        get
        {
            var profile = _vm?.Selected;
            return profile == null || string.IsNullOrWhiteSpace(profile.Host)
                ? null
                : $"{profile.Host}-{profile.Port}";
        }
    }

    public IReadOnlyList<SmartDevice> DockDevices
        => _vm?.Selected?.FlatDevices ?? (IReadOnlyList<SmartDevice>)Array.Empty<SmartDevice>();

    public IReadOnlyList<LogicRule> DockRules
        => _vm?.Selected?.LogicRules ?? (IReadOnlyList<LogicRule>)Array.Empty<LogicRule>();

    public bool IsDockLogicEngineActive => _vm?.Selected?.IsLogicEngineActive == true;

    /// <summary>
    /// Starts a rule from a dock tile.
    ///
    /// Deliberately not gated on <see cref="IsLogicEngineActiveAndWaiting"/> the way the event
    /// triggers are: the tile is already greyed out and refuses the click when the engine is
    /// off, so reaching here means the user pressed a button that said it would run.
    /// </summary>
    public void RunDockRule(string ruleId)
    {
        var rule = DockRules.FirstOrDefault(r => r.Id == ruleId);
        if (rule == null) return;

        if (!IsDockLogicEngineActive)
        {
            AppendLog("[LogicEngine] Command Dock tile ignored: the Logic Engine is not active.");
            return;
        }

        AppendLog($"[LogicEngine] Command Dock: running '{rule.Name}'.");
        _ = EnqueueRuleExecutionAsync(rule);
    }

    public async Task ToggleDockSwitchAsync(SmartDevice device, bool on)
    {
        if (device == null) return;

        // HandleDeviceToggleAsync reads the device off the sender's DataContext, which is how
        // every other caller reaches it. A bare element carrying the device satisfies that
        // without the dock having to duplicate the connect, busy and retry handling.
        var carrier = new System.Windows.FrameworkElement { DataContext = device };
        await HandleDeviceToggleAsync(carrier, on);
    }

    public IReadOnlyList<CommandDockEvent> DockEvents
        => _lastEventDockItems
            .Select(item => new CommandDockEvent(
                item.Key ?? "",
                item.Name ?? "",
                item.Icon ?? "",
                item.Active,
                item.TimerText,
                item.ToolTip))
            .ToList();

    public IReadOnlyList<CommandDockChatLine> GetDockChat(bool clan, int max)
    {
        var log = clan ? _clanChatHistoryLog : _chatHistoryLog;
        if (log == null || log.Count == 0) return Array.Empty<CommandDockChatLine>();

        return log
            .Skip(Math.Max(0, log.Count - max))
            .Select(m => new CommandDockChatLine(
                m.Author ?? "",
                m.Text ?? "",
                m.Timestamp.ToLocalTime().ToString("HH:mm"),
                m.SteamId))
            .ToList();
    }

    public (string Time, bool IsDay, TimeSpan? UntilSwitch) DockServerTime
    {
        get
        {
            var time = _vm?.ServerTime ?? "-";
            bool isDay = _vm?.IsDay ?? true;

            // TimeUntilNextPhase is already formatted for the HUD ("in 12m"), and reparsing a
            // localised string to get a TimeSpan back would break in half the shipped languages.
            // The oil rig timer is the only place that needs a real span; the clock tile shows
            // the text the HUD shows.
            return (time, isDay, null);
        }
    }

    /// <summary>The formatted "until sunrise / sunset" text the HUD shows, or empty.</summary>
    public string DockTimeUntilNextPhase => _vm?.TimeUntilNextPhase ?? "";

    /// <summary>
    /// Oil rig crate countdowns, one line per rig currently being hacked. Empty when no rule
    /// can start such a timer — a countdown nothing can ever start is worse than no tile.
    /// </summary>
    public IReadOnlyList<(string Rig, TimeSpan Left)> DockOilRigTimers
    {
        get
        {
            if (!HasOilRigTimerRule()) return Array.Empty<(string, TimeSpan)>();

            var result = new List<(string, TimeSpan)>();
            foreach (var (key, label) in new[]
                     {
                         ("Small Oil Rig", Properties.Resources.SmallOilRig),
                         ("Large Oil Rig", Properties.Resources.LargeOilRig),
                     })
            {
                var left = _monumentWatcher?.GetActiveEventTimeLeft(key);
                if (left is { } span && span > TimeSpan.Zero) result.Add((label, span));
            }
            return result;
        }
    }
}
