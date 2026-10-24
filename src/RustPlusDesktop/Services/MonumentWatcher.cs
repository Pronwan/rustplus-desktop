using RustPlusDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services
{
    public class MonumentWatcher
    {
        // Status des Events (Countdown)
        private class ActiveEvent
        {
            public DateTime EndTime { get; set; }
            public bool Announce15Min { get; set; } = false;
            public bool Announce10Min { get; set; } = false;
            public bool Announce5Min { get; set; } = false;

            /// <summary>Whole minutes the timer was started with. Reminders at or above this
            /// are suppressed, because the start announcement has already said that number.</summary>
            public double TotalMinutes { get; set; }

        }

        private (double X, double Y)? _smallOilPos;
        private (double X, double Y)? _largeOilPos;

        public const int VIRTUAL_CRATE_TYPE = 150;

        private Dictionary<string, ActiveEvent> _activeEvents = new();

        public event EventHandler<(string Name, int Duration)>? OnOilRigTriggered;
        public event EventHandler<string>? OnOilRigChatUpdate;

        public bool HasSmallOil => _smallOilPos.HasValue;
        public bool HasLargeOil => _largeOilPos.HasValue;

        public bool HasAnyMonument => HasSmallOil || HasLargeOil;

        public void SetMonuments(List<RustPlusClientReal.DynMarker> monuments)
        {
            foreach (var m in monuments)
            {
                if (m.X < 1 && m.Y < 1) continue;

                var name = (m.Label ?? "").ToLowerInvariant();
                if (name.Contains("oil") && name.Contains("small")) _smallOilPos = (m.X, m.Y);
                if (name.Contains("large") && name.Contains("oil")) _largeOilPos = (m.X, m.Y);
            }
        }

        public List<RustPlusClientReal.DynMarker> UpdateAndGetVirtualMarkers(List<RustPlusClientReal.DynMarker> currentMarkers, HashSet<uint> ignoredKnownIds)
        {
            var virtualMarkers = new List<RustPlusClientReal.DynMarker>();
            var now = DateTime.UtcNow;

            // --- Update & clean up events (timer logic) ---
            var toRemove = new List<string>();

            foreach (var kv in _activeEvents)
            {
                var rigName = kv.Key;
                var evt = kv.Value;

                if (evt.EndTime < now)
                {
                    toRemove.Add(rigName);
                    continue;
                }

                var timeLeft = evt.EndTime - now;
                double minutesLeft = timeLeft.TotalMinutes;

                var localizedRigName = rigName == "Small Oil Rig" ? Properties.Resources.SmallOilRig :
                                       rigName == "Large Oil Rig" ? Properties.Resources.LargeOilRig :
                                       rigName;

                // Reminders only below the configured length. A 10-minute timer announces
                // "10 minutes" when it starts, so repeating it a second later would be noise —
                // and a 15-minute reminder on it would be a lie.
                if (minutesLeft <= 15.0 && minutesLeft > 14.0 && !evt.Announce15Min && evt.TotalMinutes > 15.0)
                {
                    evt.Announce15Min = true;
                    OnOilRigChatUpdate?.Invoke(this, AlertTemplateService.GetFormattedAlert("AlertCrateUnlocksIn15Min", localizedRigName));
                }

                // 10 Min Warnung
                if (minutesLeft <= 10.0 && minutesLeft > 9.0 && !evt.Announce10Min && evt.TotalMinutes > 10.0)
                {
                    evt.Announce10Min = true;
                    OnOilRigChatUpdate?.Invoke(this, AlertTemplateService.GetFormattedAlert("AlertCrateUnlocksIn10Min", localizedRigName));
                }

                // 5 Min Warnung
                if (minutesLeft <= 5.0 && minutesLeft > 4.0 && !evt.Announce5Min && evt.TotalMinutes > 5.0)
                {
                    evt.Announce5Min = true;
                    OnOilRigChatUpdate?.Invoke(this, AlertTemplateService.GetFormattedAlert("AlertCrateUnlocksIn5Min", localizedRigName));
                }

            }

            foreach (var key in toRemove) _activeEvents.Remove(key);

            return virtualMarkers;
        }

        /// <summary>Returns the remaining time on the active hack timer for the given rig, or null if not active.</summary>
        public TimeSpan? GetActiveEventTimeLeft(string rigName)
        {
            if (_activeEvents.TryGetValue(rigName, out var evt))
            {
                var remaining = evt.EndTime - DateTime.UtcNow;
                return remaining.TotalSeconds > 0 ? remaining : TimeSpan.Zero;
            }
            return null;
        }

        public void Reset()
        {
            _activeEvents.Clear();
            _smallOilPos = null;
            _largeOilPos = null;
        }

        /// <summary>
        /// Starts a player-configured Logic Engine countdown without any map marker or detection.
        /// </summary>
        /// <param name="rigName">"Small Oil Rig" or "Large Oil Rig".</param>
        /// <param name="showCrate">Legacy argument; map markers are always disabled.</param>
        /// <returns>False if that rig already has a running timer, which is not overwritten.</returns>
        public bool TriggerExternal(string rigName, int durationSeconds, bool showCrate)
        {
            if (string.IsNullOrWhiteSpace(rigName) || durationSeconds <= 0) return false;

            // A second alarm while one is running is the same hack being re-announced, not a
            // new one. Restarting would push the countdown past the real unlock.
            if (_activeEvents.TryGetValue(rigName, out var running) && running.EndTime > DateTime.UtcNow)
                return false;

            TriggerEvent(rigName, durationSeconds);
            return true;
        }

        private void TriggerEvent(string rigName, int durationSeconds)
        {
            var evt = new ActiveEvent
            {
                EndTime = DateTime.UtcNow.AddSeconds(durationSeconds),
                Announce15Min = false,
                Announce10Min = false,
                Announce5Min = false,
                TotalMinutes = durationSeconds / 60.0,
            };

            _activeEvents[rigName] = evt;
            OnOilRigTriggered?.Invoke(this, (rigName, durationSeconds));
        }
    }
}
