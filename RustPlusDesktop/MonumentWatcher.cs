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
        }

        private (double X, double Y)? _smallOilPos;
        private (double X, double Y)? _largeOilPos;

        public const int VIRTUAL_CRATE_TYPE = 150;

        private Dictionary<string, ActiveEvent> _activeEvents = new();

        // Wir tracken jetzt kompletten State pro Chinook-ID
        private Dictionary<uint, ChinookState> _chinookStates = new();

        // --- KONFIGURATION ---

        // 1. Maximale Distanz des SPAWN-PUNKTS zum Rig. 
        // Du sagtest "1,5 Grids" (ca. 225m). Wir nehmen sicherheitshalber 600m (~4 Grids),
        // da Spawns manchmal etwas variieren. Alles darüber ist ein Roamer.
        private const double MaxSpawnDistance = 600.0;

        // 2. Wie lange nach dem Spawn darf getriggert werden?
        // Wenn er älter als 60 Sek ist, ist es kein direkter Call mehr.
        private const double MaxSpawnAgeSeconds = 60.0;

        // 3. Wie genau muss er drauf zufliegen? (0.9 = sehr genau)
        private const double ApproachAngleThreshold = 0.9;

        // Timer: 15 Min + 60s Anflug
        private const int HackDurationSeconds = 900;

        public event EventHandler<string> OnOilRigTriggered;
        public event EventHandler<string> OnOilRigChatUpdate;

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
                        // NEUER Chinook! (Spawn erkannt)
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

                    // 2. Trigger prüfen
                    // NUR wenn Chinook "jung" ist (< 60s seit wir ihn kennen)
                    // Alte Chinooks werden ignoriert (selbst wenn sie später übers Rig fliegen).
                    if ((now - state.FirstSeen).TotalSeconds < MaxSpawnAgeSeconds)
                    {
                        if (!_activeEvents.ContainsKey("Small Oil Rig"))
                            CheckAndTrigger(ch47, state, _smallOilPos, "Small Oil Rig");

                        if (!_activeEvents.ContainsKey("Large Oil Rig"))
                            CheckAndTrigger(ch47, state, _largeOilPos, "Large Oil Rig");
                    }

                    // Position für nächsten Tick merken
                    state.LastX = ch47.X;
                    state.LastY = ch47.Y;
                }

                // Veraltete States aufräumen (Chinook despawned / zerstört)
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

        private void CheckAndTrigger(RustPlusClientReal.DynMarker chinook, ChinookState state, (double X, double Y)? rigPos, string rigName)
        {
            if (rigPos == null) return;

            // A: Spawn-Distanz Check
            // War der ORT, wo er gespawnt ist (FirstX/Y), nah am Rig?
            double spawnDx = rigPos.Value.X - state.FirstX;
            double spawnDy = rigPos.Value.Y - state.FirstY;
            double spawnDist = Math.Sqrt(spawnDx * spawnDx + spawnDy * spawnDy);

            // Wenn er weiter als MaxSpawnDistance (z.B. 600m) vom Rig entfernt GESPAWNT ist: Ignorieren.
            // Das filtert Roamer aus, die am Airfield gespawnt sind.
            if (spawnDist > MaxSpawnDistance) return;

            // B: Bewegungs-Check (Richtung)
            // Bewegt er sich JETZT auf das Rig zu?

            // Vektor Bewegung (letzter Tick -> jetzt)
            double moveX = chinook.X - state.LastX;
            double moveY = chinook.Y - state.LastY;
            double moveLen = Math.Sqrt(moveX * moveX + moveY * moveY);

            if (moveLen < 0.1) return; // Schwebt/Laggt noch

            // Vektor zum Rig (von aktueller Pos)
            double toRigX = rigPos.Value.X - chinook.X;
            double toRigY = rigPos.Value.Y - chinook.Y;
            double toRigLen = Math.Sqrt(toRigX * toRigX + toRigY * toRigY);

            // Dot Product für Winkel
            double dot = (moveX / moveLen) * (toRigX / toRigLen) + (moveY / moveLen) * (toRigY / toRigLen);

            // Wenn er nah gespawnt ist (<600m) UND präzise drauf zufliegt (>0.9)
            if (dot > ApproachAngleThreshold)
            {
                TriggerEvent(rigName);
            }
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