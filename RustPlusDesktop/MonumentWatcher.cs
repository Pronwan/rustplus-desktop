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
            public bool Announce10Min { get; set; } = false;
            public bool Announce5Min { get; set; } = false;
        }

        // Status eines Chinooks (um Spawn-Zeit und Ort zu tracken)
        private class ChinookState
        {
            public DateTime FirstSeen { get; set; } // Wann tauchte er auf?
            public double FirstX { get; set; }      // Wo tauchte er auf?
            public double FirstY { get; set; }

            public double LastX { get; set; }       // Letzte Position (für Vektor)
            public double LastY { get; set; }
            public bool DebugLogged { get; set; }
        }

        private (double X, double Y)? _smallOilPos;
        private (double X, double Y)? _largeOilPos;

        public const int VIRTUAL_CRATE_TYPE = 150;

        private Dictionary<string, ActiveEvent> _activeEvents = new();

        // Wir tracken jetzt kompletten State pro Chinook-ID
        private Dictionary<uint, ChinookState> _chinookStates = new();

        // --- KONFIGURATION ---

        // 1. Maximale Distanz zum Rig für Trigger (Hover-Radius)
        private const double TriggerRadius = 200.0;

        // 2. Maximale Geschwindigkeit für "Hover" (Einheiten pro Tick)
        // Chinooks fliegen schnell (>5-10u/tick). Wenn er < 2.0 ist, steht er fast.
        private const double MaxHoverSpeed = 2.0;

        // Timer: 14 Min 15 Sek (855s)
        private const int HackDurationSeconds = 855;

        public event EventHandler<string> OnOilRigTriggered;
        public event EventHandler<string> OnOilRigChatUpdate;
        public event EventHandler<string>? OnDebug;

        public bool HasMonuments =>
            _smallOilPos.HasValue && _smallOilPos.Value.X > 1 &&
            _largeOilPos.HasValue && _largeOilPos.Value.X > 1;

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

            if (HasMonuments)
            {
                var chinooks = currentMarkers.Where(m => m.Type == 4 || m.Kind.Contains("CH47"));
                var currentChinookIds = new HashSet<uint>();

                foreach (var ch47 in chinooks)
                {
                    if (ch47.X < 1 && ch47.Y < 1) continue;
                    currentChinookIds.Add(ch47.Id);

                    // 1. Chinook State holen oder neu anlegen
                    if (!_chinookStates.TryGetValue(ch47.Id, out var state))
                    {
                        // NEUER Chinook!
                        state = new ChinookState
                        {
                            FirstSeen = now,
                            FirstX = ch47.X,
                            FirstY = ch47.Y,
                            LastX = ch47.X,
                            LastY = ch47.Y
                        };
                        _chinookStates[ch47.Id] = state;
                    }

                    // 2. Trigger prüfen (Hover Logic)
                    if (!_activeEvents.ContainsKey("Small Oil Rig"))
                        CheckAndTriggerHover(ch47, state, _smallOilPos, "Small Oil Rig");

                    if (!_activeEvents.ContainsKey("Large Oil Rig"))
                        CheckAndTriggerHover(ch47, state, _largeOilPos, "Large Oil Rig");

                    // Position für nächsten Tick merken
                    state.LastX = ch47.X;
                    state.LastY = ch47.Y;
                }

                // Veraltete States aufräumen
                var oldIds = _chinookStates.Keys.Where(k => !currentChinookIds.Contains(k)).ToList();
                foreach (var id in oldIds) _chinookStates.Remove(id);
            }

            // --- Events Updaten & Aufräumen (Timer Logic) ---
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

                // 10 Min Warnung
                if (minutesLeft <= 10.0 && minutesLeft > 9.0 && !evt.Announce10Min)
                {
                    evt.Announce10Min = true;
                    OnOilRigChatUpdate?.Invoke(this, $"[{rigName}] Crate unlocks in 10 minutes!");
                }

                // 5 Min Warnung
                if (minutesLeft <= 5.0 && minutesLeft > 4.0 && !evt.Announce5Min)
                {
                    evt.Announce5Min = true;
                    OnOilRigChatUpdate?.Invoke(this, $"[{rigName}] Crate unlocks in 5 minutes!");
                }

                // Position für Marker
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

        private void CheckAndTriggerHover(RustPlusClientReal.DynMarker chinook, ChinookState state, (double X, double Y)? rigPos, string rigName)
        {
            if (rigPos == null) return;

            // 1. Distanz zum Rig
            double dx = rigPos.Value.X - chinook.X;
            double dy = rigPos.Value.Y - chinook.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // 2. Geschwindigkeit (Weg seit letztem Tick)
            double moveX = chinook.X - state.LastX;
            double moveY = chinook.Y - state.LastY;
            double speed = Math.Sqrt(moveX * moveX + moveY * moveY);

            // LOGIK: Wenn er nah ist (<200m) UND langsam (<2.0)
            if (dist < TriggerRadius && speed < MaxHoverSpeed)
            {
                TriggerEvent(rigName);
                OnDebug?.Invoke(this, $"[MON] Triggered {rigName}! Hovering: Dist={dist:F1} Speed={speed:F2}");
            }
            else
            {
                // Debugging (nur wenn nah dran)
                if (dist < 500 && !state.DebugLogged)
                {
                     // Wir loggen nur einmal, wenn er in die Nähe kommt, damit wir sehen, was passiert
                    OnDebug?.Invoke(this, $"[MON] CH={chinook.Id} near {rigName} (Dist={dist:F0}), Speed={speed:F2} (Req < {MaxHoverSpeed})");
                    // Wir resetten das Log flag, wenn er weit weg ist, aber hier speichern wir es, damit wir nicht spammen während er vorbeifliegt.
                    // Bei Hover Logic ist "state.DebugLogged" etwas schwieriger zu managen.
                    // Wir loggen es einfach alle paar Sekunden via Random oder Counter wäre besser, aber state.DebugLogged hilft für 'einmal pro Anflug'.
                    state.DebugLogged = true; 
                }
            }
        }

        public void Reset()
        {
            _activeEvents.Clear();
            _chinookStates.Clear();
            _smallOilPos = null;
            _largeOilPos = null;
        }

        private void TriggerEvent(string rigName)
        {
            var evt = new ActiveEvent
            {
                EndTime = DateTime.UtcNow.AddSeconds(HackDurationSeconds),
                Announce10Min = false,
                Announce5Min = false
            };

            _activeEvents[rigName] = evt;
            OnOilRigTriggered?.Invoke(this, rigName);
        }
    }
}