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
            public double TotalMinutes { get; set; } = HackDurationSeconds / 60.0;

            /// <summary>False hides the crate marker but leaves alerts and commands alone.</summary>
            public bool ShowCrate { get; set; } = true;
        }

        // State of a Chinook (to track spawn time and location)
        private class ChinookState
        {
            public DateTime FirstSeen { get; set; } // Wann tauchte er auf?
            public double FirstX { get; set; }      // Wo tauchte er auf?
            public double FirstY { get; set; }

            public double LastX { get; set; }       // Last position (for vector)
            public double LastY { get; set; }
            public DateTime LastSeen { get; set; }  // When last seen?
            public int MissingCount { get; set; }
            public int TickCount { get; set; }
            public bool TrajectoryTriggered { get; set; }
            public bool DebugLogged { get; set; }
        }

        private (double X, double Y)? _smallOilPos;
        private (double X, double Y)? _largeOilPos;

        public const int VIRTUAL_CRATE_TYPE = 150;

        private Dictionary<string, ActiveEvent> _activeEvents = new();

        // Wir tracken jetzt kompletten State pro Chinook-ID
        private Dictionary<uint, ChinookState> _chinookStates = new();

        // When each rig was last triggered (session memory for !oilrig)
        private readonly Dictionary<string, DateTime> _lastTriggeredTimes = new();

        // --- KONFIGURATION ---

        // 1. Maximum distance to the rig for a trigger (hover radius)
        private const double TriggerRadius = 300.0;

        // 2. Maximum speed for "hover" (units per second)
        private const double MaxHoverSpeed = 4.0;

        // Timer: 14 Min 15 Sek (855s) - Standard for early hover trigger
        private const int HackDurationSeconds = 855;

        public event EventHandler<(string Name, int Duration)>? OnOilRigTriggered;
        public event EventHandler<string>? OnOilRigChatUpdate;
        public event EventHandler<string>? OnDebug;

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

            if (HasAnyMonument)
            {
                var chinooks = currentMarkers.Where(m => m.Type == 4 || (m.Kind?.Contains("CH47", StringComparison.OrdinalIgnoreCase) == true));
                var currentChinookIds = new HashSet<uint>();

                foreach (var ch47 in chinooks)
                {
                    if (ch47.X < 1 && ch47.Y < 1) continue;
                    currentChinookIds.Add(ch47.Id);

                    if (!_chinookStates.TryGetValue(ch47.Id, out var state))
                    {
                        state = new ChinookState
                        {
                            FirstSeen = now,
                            FirstX = ch47.X,
                            FirstY = ch47.Y,
                            LastX = ch47.X,
                            LastY = ch47.Y,
                            LastSeen = now,
                            MissingCount = 0
                        };
                        _chinookStates[ch47.Id] = state;
                    }

                    state.MissingCount = 0;

                    // Check the trigger for each existing rig individually
                    if (HasSmallOil && !_activeEvents.ContainsKey("Small Oil Rig"))
                    {
                        CheckAndTriggerHover(ch47, state, _smallOilPos, "Small Oil Rig", now);
                        CheckAndTriggerTrajectory(ch47, state, _smallOilPos, "Small Oil Rig", now);
                    }

                    if (HasLargeOil && !_activeEvents.ContainsKey("Large Oil Rig"))
                    {
                        CheckAndTriggerHover(ch47, state, _largeOilPos, "Large Oil Rig", now);
                        CheckAndTriggerTrajectory(ch47, state, _largeOilPos, "Large Oil Rig", now);
                    }

                    state.LastX = ch47.X;
                    state.LastY = ch47.Y;
                    state.LastSeen = now;
                }

                var oldIds = _chinookStates.Keys.Where(k => !currentChinookIds.Contains(k)).ToList();
                foreach (var id in oldIds)
                {
                    if (++_chinookStates[id].MissingCount > 15) _chinookStates.Remove(id);
                }
            }

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

                // Alerts above have already run: hiding the crate is a map decision, not a
                // reason to go quiet.
                if (!evt.ShowCrate) continue;

                // Position for the marker
                double x = 0, y = 0;
                if (rigName == "Small Oil Rig" && _smallOilPos.HasValue) { x = _smallOilPos.Value.X; y = _smallOilPos.Value.Y; }
                else if (rigName == "Large Oil Rig" && _largeOilPos.HasValue) { x = _largeOilPos.Value.X; y = _largeOilPos.Value.Y; }
                else continue;

                // ID generieren (Bit-Maske gegen Kollision)
                uint vId = 0xB0000000 | (uint)rigName.GetHashCode();
                string timeStr = $"{(int)minutesLeft}:{timeLeft.Seconds:D2}";

                virtualMarkers.Add(new RustPlusClientReal.DynMarker(
                    id: vId,
                    type: VIRTUAL_CRATE_TYPE,
                    kind: "Locked Crate",
                    x: x,
                    y: y,
                    label: timeStr,
                    name: null,
                    steamId: 0
                ));
            }

            foreach (var key in toRemove) _activeEvents.Remove(key);

            return virtualMarkers;
        }

        private void CheckAndTriggerHover(RustPlusClientReal.DynMarker chinook, ChinookState state, (double X, double Y)? rigPos, string rigName, DateTime now)
        {
            if (rigPos == null) return;

            // 1. Distance to the rig
            double dx = rigPos.Value.X - chinook.X;
            double dy = rigPos.Value.Y - chinook.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // 2. Speed (distance since last tick)
            double moveX = chinook.X - state.LastX;
            double moveY = chinook.Y - state.LastY;
            double moveDist = Math.Sqrt(moveX * moveX + moveY * moveY);
            
            // Compute real speed (units per second) to compensate for lag
            double elapsedSeconds = (now - state.LastSeen).TotalSeconds;
            if (elapsedSeconds <= 0) elapsedSeconds = 1.0; // Fallback for the same tick

            double speed = moveDist / elapsedSeconds;

            // LOGIC: When it is near (<200m) AND slow (<2.0)
            if (dist < TriggerRadius && speed < MaxHoverSpeed)
            {
                TriggerEvent(rigName, HackDurationSeconds);
                OnDebug?.Invoke(this, $"[MON] Triggered {rigName}! Hovering: Dist={dist:F1} Speed={speed:F2}");
            }
            else
            {
                if (dist < 500 && !state.DebugLogged)
                {
                    OnDebug?.Invoke(this, $"[MON] CH={chinook.Id} near {rigName} (Dist={dist:F0}), Speed={speed:F2}");
                    state.DebugLogged = true;
                }
            }
        }
        
        private void CheckAndTriggerTrajectory(RustPlusClientReal.DynMarker chinook, ChinookState state, (double X, double Y)? rigPos, string rigName, DateTime now)
        {
            if (rigPos == null || state.TrajectoryTriggered) return;

            // 1. Distance from the rig at spawn
            double dxS = state.FirstX - rigPos.Value.X;
            double dyS = state.FirstY - rigPos.Value.Y;
            double spawnDist = Math.Sqrt(dxS * dxS + dyS * dyS);

            // 2. Current distance to the rig
            double dxC = chinook.X - rigPos.Value.X;
            double dyC = chinook.Y - rigPos.Value.Y;
            double currentDist = Math.Sqrt(dxC * dxC + dyC * dyC);

            // LOGIC: Chinook appears near the rig (extended radius up to ~8 grids)
            // and then flies away. Radius: between 50m and 1200m at spawn.
            if (spawnDist > 50 && spawnDist < 1200)
            {
                state.TickCount++;

                // Debug log every few ticks so user sees it's working
                if (state.TickCount == 1 || state.TickCount % 5 == 0)
                {
                    OnDebug?.Invoke(this, $"[MON] Evaluating Trajectory for {rigName}: SpawnDist={spawnDist:F0} Tick={state.TickCount} CurrentDist={currentDist:F0}");
                }

                // We wait ~3 ticks (approx. 6-10 sec) to confirm the flight direction
                if (state.TickCount >= 3)
                {
                    double moveX = chinook.X - state.FirstX;
                    double moveY = chinook.Y - state.FirstY;
                    double totalMoved = Math.Sqrt(moveX * moveX + moveY * moveY);

                    // It must have moved somewhat away from the spawn
                    // AND the distance to the rig must have increased.
                    if (totalMoved > 100 && currentDist > spawnDist + 50)
                    {
                        // Angle check: is it moving away from the rig?
                        double angle = GetAngle(dxS, dyS, moveX, moveY);
                        
                        if (Math.Abs(angle) < 35) // Within 35 degrees deviation
                        {
                            state.TrajectoryTriggered = true;
                            TriggerEvent(rigName, 750); // 12 Minuten 30 Sekunden
                            OnDebug?.Invoke(this, $"[MON] !!! Trajectory Trigger !!! {rigName} SpawnDist={spawnDist:F0} Moved={totalMoved:F0} Angle={angle:F1}");
                        }
                    }
                }
            }
        }

        private double GetAngle(double x1, double y1, double x2, double y2)
        {
            double dot = x1 * x2 + y1 * y2;
            double mag1 = Math.Sqrt(x1 * x1 + y1 * y1);
            double mag2 = Math.Sqrt(x2 * x2 + y2 * y2);
            if (mag1 < 0.001 || mag2 < 0.001) return 0;
            double cos = dot / (mag1 * mag2);
            if (cos > 1) cos = 1; if (cos < -1) cos = -1;
            return Math.Acos(cos) * (180.0 / Math.PI);
        }

        /// <summary>Returns when the given rig was last triggered this session, or null if never.</summary>
        public DateTime? GetLastTriggered(string rigName)
            => _lastTriggeredTimes.TryGetValue(rigName, out var t) ? t : null;

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
            _chinookStates.Clear();
            _smallOilPos = null;
            _largeOilPos = null;
        }

        /// <summary>
        /// Starts a hack timer from outside the Chinook tracking.
        ///
        /// Exists because the Chinook markers this class was built on stopped arriving. A
        /// player can tune an RF receiver to a rig's frequency and wire it to a Smart Alarm,
        /// which the Logic Engine turns into this call — the game itself becomes the sensor.
        /// Everything downstream is deliberately unchanged: same crate marker, same reminders,
        /// same answer to the chat command.
        /// </summary>
        /// <param name="rigName">"Small Oil Rig" or "Large Oil Rig".</param>
        /// <param name="showCrate">False draws no marker but keeps alerts and commands.</param>
        /// <returns>False if that rig already has a running timer, which is not overwritten.</returns>
        public bool TriggerExternal(string rigName, int durationSeconds, bool showCrate)
        {
            if (string.IsNullOrWhiteSpace(rigName) || durationSeconds <= 0) return false;

            // A second alarm while one is running is the same hack being re-announced, not a
            // new one. Restarting would push the countdown past the real unlock.
            if (_activeEvents.TryGetValue(rigName, out var running) && running.EndTime > DateTime.UtcNow)
                return false;

            TriggerEvent(rigName, durationSeconds, showCrate);
            return true;
        }

        private void TriggerEvent(string rigName, int durationSeconds = HackDurationSeconds, bool showCrate = true)
        {
            var evt = new ActiveEvent
            {
                EndTime = DateTime.UtcNow.AddSeconds(durationSeconds),
                Announce15Min = false,
                Announce10Min = false,
                Announce5Min = false,
                TotalMinutes = durationSeconds / 60.0,
                ShowCrate = showCrate
            };

            _activeEvents[rigName] = evt;
            _lastTriggeredTimes[rigName] = DateTime.UtcNow;
            OnOilRigTriggered?.Invoke(this, (rigName, durationSeconds));
        }
    }
}
