using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Everything the mini-map's command dock needs from the main window.
    ///
    /// The dock polls rather than subscribes: half its content is a countdown that has to tick
    /// on its own anyway, and one timer that re-reads current state is both simpler and harder
    /// to leak than six event subscriptions across a window that opens and closes freely.
    /// </summary>
    public interface ICommandDockHost
    {
        /// <summary>
        /// The active server as "{host}-{port}", or null when none is selected. Device tiles are
        /// stamped with it so they only appear where their entity ids mean something.
        /// </summary>
        string? DockServerKey { get; }

        /// <summary>Every paired device on the active server, groups flattened.</summary>
        IReadOnlyList<SmartDevice> DockDevices { get; }

        /// <summary>The active server's Logic Engine rules, enabled or not.</summary>
        IReadOnlyList<LogicRule> DockRules { get; }

        /// <summary>False while the Logic Engine's master switch is off — rule tiles grey out.</summary>
        bool IsDockLogicEngineActive { get; }

        /// <summary>Runs a rule as if a Command Dock trigger had fired.</summary>
        void RunDockRule(string ruleId);

        /// <summary>Flips a smart switch through the same guarded path the device list uses.</summary>
        Task ToggleDockSwitchAsync(SmartDevice device, bool on);

        /// <summary>The event dock's current entries, keyed by <c>cargo</c>, <c>deepsea</c>, …</summary>
        IReadOnlyList<CommandDockEvent> DockEvents { get; }

        /// <summary>The last <paramref name="max"/> lines of team or clan chat, oldest first.</summary>
        IReadOnlyList<CommandDockChatLine> GetDockChat(bool clan, int max);

        /// <summary>Server time as "HH:mm" plus whether it is currently day.</summary>
        (string Time, bool IsDay, TimeSpan? UntilSwitch) DockServerTime { get; }

        /// <summary>
        /// The notification channels the user has configured for their Discord bot — "chat",
        /// "events", "shop" — as the destinations the Discord tile can send to. Empty when the
        /// bot is not set up, which is what greys the tile out.
        /// </summary>
        Task<IReadOnlyList<string>> GetDockDiscordChannelsAsync();

        /// <summary>Posts a line into one of those channels.</summary>
        Task<bool> SendDockDiscordMessageAsync(string channelType, string message);

        /// <summary>Posts the current map view into one of those channels.</summary>
        Task<bool> SendDockMapToDiscordAsync(string channelType);
    }

    /// <summary>An event dock entry, flattened so the dock does not depend on the main window's
    /// private item struct.</summary>
    public sealed record CommandDockEvent(
        string Key,
        string Name,
        string Icon,
        bool Active,
        string? TimerText,
        string? ToolTip);

    public sealed record CommandDockChatLine(string Author, string Message, string Time, ulong SteamId);
}
