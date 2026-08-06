using System;
using System.Collections.Generic;
using System.Linq;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>
    /// Builds the event dock from crowd-sourced audio detections instead of map markers.
    ///
    /// Replaces the whole list rather than threading conditionals through the marker-based
    /// builder: the two sources share nothing but the dock item shape. The API path keeps
    /// positions, routes and per-event timers; this one has a timestamp and a confirmation
    /// count, and pretending otherwise is how misleading UI gets written.
    ///
    /// Every item is Trackable = false. There are no coordinates behind any of this, so
    /// clicking to centre the map would move it somewhere arbitrary.
    /// </summary>
    private List<EventDockItem> BuildCloudEventDockItems()
    {
        var items = new List<EventDockItem>();
        var watcher = CloudEventWatcher.Instance;

        // ---- Cargo: spawn time only. Docking, harbour and departure all needed positions.
        if (EventCapabilities.IsTrackable(RustEventKind.Cargo))
        {
            var cargo = watcher.Get(RustEventKind.Cargo);
            string timer = "";
            string tip = Properties.Resources.EventNotDetected;

            if (cargo != null)
            {
                TimeSpan age = cargo.Age;
                timer = cargo.IsActive
                    ? $"{(int)age.TotalMinutes}:{age.Seconds:D2}"
                    : $"-{(int)(DateTime.UtcNow - cargo.ExpiresAtUtc).TotalMinutes}:{(DateTime.UtcNow - cargo.ExpiresAtUtc).Seconds:D2}";
                tip = cargo.IsActive
                    ? string.Format(Properties.Resources.CargoSpawnedAgo, FormatAgo(age))
                    // Not CargoDespawnedAgo — that one takes two placeholders ("{0}m {1}s")
                    // and would throw a FormatException with a single pre-formatted span.
                    : string.Format(Properties.Resources.CargoGoneAgo, FormatAgo(DateTime.UtcNow - cargo.ExpiresAtUtc));
                tip = AppendConfidence(tip, cargo);
            }

            items.Add(new EventDockItem
            {
                Name = Properties.Resources.CargoShip,
                Icon = "pack://application:,,,/Assets/icons/cargo.png",
                Active = cargo?.IsActive == true,
                Id = 0, X = 0, Y = 0, Trackable = false, Type = 5,
                TimerText = timer,
                ToolTip = tip,
            });
        }

        // ---- Deep Sea: runs exactly three hours, so the close is arithmetic. Counts down
        //      while open, then counts up since it closed — respawn timing is variable.
        if (EventCapabilities.IsTrackable(RustEventKind.DeepSea))
        {
            var deepSea = watcher.Get(RustEventKind.DeepSea);
            string timer = "";
            string tip = Properties.Resources.EventNotDetected;

            if (deepSea != null)
            {
                if (deepSea.IsActive)
                {
                    TimeSpan left = deepSea.Remaining;
                    timer = $"{(int)left.TotalMinutes}:{left.Seconds:D2}";
                    tip = string.Format(Properties.Resources.DeepSeaUpFor, FormatAgo(deepSea.Age));
                }
                else
                {
                    TimeSpan since = DateTime.UtcNow - deepSea.ExpiresAtUtc;
                    timer = $"-{(int)since.TotalMinutes}:{since.Seconds:D2}";
                    tip = string.Format(Properties.Resources.DeepSeaEndedAgo, FormatAgo(since));
                }
                tip = AppendConfidence(tip, deepSea);
            }

            items.Add(new EventDockItem
            {
                Name = Properties.Resources.DeepSea,
                Icon = "pack://application:,,,/Assets/icons/ds_event.png",
                Active = deepSea?.IsActive == true,
                Id = 0, X = 0, Y = 0, Trackable = false, Type = 0,
                TimerText = timer,
                ToolTip = tip,
            });
        }

        // ---- Oil Rig: the cue announces "a crate is up", not a spawn, and Small and Large
        //      share one sound. So no state is claimed — only when a crate was last heard,
        //      for the two most recent detections. Which rig it was is unknowable.
        if (EventCapabilities.IsTrackable(RustEventKind.OilRig))
        {
            var oilRig = watcher.Get(RustEventKind.OilRig);
            string timer = "";
            string tip = Properties.Resources.EventNotDetected;

            if (oilRig != null && oilRig.RecentUtc.Count > 0)
            {
                TimeSpan sinceLast = DateTime.UtcNow - oilRig.RecentUtc[0];
                timer = $"{(int)sinceLast.TotalMinutes}:{sinceLast.Seconds:D2}";

                tip = string.Join("\n", oilRig.RecentUtc
                    .Take(2)
                    .Select(seen => $"{Properties.Resources.OilRigCrateSeen}: {FormatAgo(DateTime.UtcNow - seen)}"));
                tip = AppendConfidence(tip, oilRig);
            }

            items.Add(new EventDockItem
            {
                // Not Resources.OilRig — that reads "Oil Rig Trigger", which described a player
                // calling the rig via the API. Here it is a recurring "a crate is up" cue, and
                // the wording matches the alert menu so both call it the same thing.
                Name = Properties.Resources.OilRigCrateStatus,
                Icon = "pack://application:,,,/Assets/icons/crate.png",
                Active = oilRig?.IsActive == true,
                Id = 0, X = 0, Y = 0, Trackable = false, Type = 0,
                TimerText = timer,
                ToolTip = tip,
            });
        }

        return items;
    }

    /// <summary>
    /// Marks a single unverified report as such. The dock shows it — hiding it would be worse
    /// on a quiet server where quorum can never be reached — but chat and Discord stay silent
    /// until it is confirmed.
    /// </summary>
    private static string AppendConfidence(string tip, CloudEventState state) =>
        state.IsConfirmed ? tip : $"{tip}\n({Properties.Resources.EventUnconfirmed})";
}
