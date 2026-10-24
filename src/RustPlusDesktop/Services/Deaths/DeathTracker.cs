using System;
using System.Collections.Generic;

namespace RustPlusDesk.Services.Deaths
{
    /// <summary>
    /// Turns successive Rust+ team-info snapshots into death events.
    ///
    /// A death is detected when a member's DeathTime advances (authoritative), or
    /// — when the proto carries no DeathTime — on an alive→dead transition,
    /// stamped with the current time. The first snapshot only establishes a
    /// baseline: deaths already present when the app connects are history, not new
    /// events. Per-member dedupe on the last recorded DeathTime stops the same
    /// death being emitted on every poll while the member stays dead.
    /// </summary>
    public sealed class DeathTracker
    {
        private readonly Dictionary<ulong, long> _lastDeathTime = new();
        private readonly Dictionary<ulong, long> _spawnObserved = new();
        private readonly Dictionary<ulong, bool> _wasDead = new();
        private bool _baselineEstablished;

        /// <summary>Reset when switching servers so one server's state can't leak into another.</summary>
        public void Reset()
        {
            _lastDeathTime.Clear();
            _spawnObserved.Clear();
            _wasDead.Clear();
            _baselineEstablished = false;
        }

        public IReadOnlyList<DeathRecord> Observe(RustPlusClientReal.TeamInfo team, DeathLocationClassifier classifier)
        {
            var deaths = new List<DeathRecord>();

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (var member in team.Members)
            {
                if (member.SteamId == 0)
                    continue;

                bool wasDead = _wasDead.TryGetValue(member.SteamId, out var wd) && wd;

                // A dead→alive edge is a respawn — remember when, so the next death's
                // survival time can be derived even when the proto omits SpawnTime.
                if (wasDead && !member.Dead)
                    _spawnObserved[member.SteamId] = member.SpawnTime is > 0 ? member.SpawnTime.Value : now;

                _wasDead[member.SteamId] = member.Dead;

                bool isNewDeath;
                long deathTime;

                if (member.DeathTime is > 0)
                {
                    long last = _lastDeathTime.TryGetValue(member.SteamId, out var lt) ? lt : 0;
                    isNewDeath = member.DeathTime.Value > last;
                    deathTime = member.DeathTime.Value;
                }
                else
                {
                    // No authoritative timestamp — treat the alive→dead edge as the death.
                    isNewDeath = member.Dead && !wasDead;
                    deathTime = now;
                }

                if (!isNewDeath)
                    continue;

                _lastDeathTime[member.SteamId] = deathTime;

                // Record baseline deaths silently; only emit ones seen since connect.
                if (!_baselineEstablished)
                    continue;

                // Prefer the proto's spawn time; otherwise the last respawn we saw.
                long? spawnTime = member.SpawnTime is > 0
                    ? member.SpawnTime.Value
                    : (_spawnObserved.TryGetValue(member.SteamId, out var so) ? so : (long?)null);

                var (type, name) = classifier.Classify(member.X, member.Y);
                deaths.Add(new DeathRecord(
                    member.SteamId,
                    member.Name,
                    deathTime,
                    spawnTime,
                    member.X,
                    member.Y,
                    classifier.Grid(member.X, member.Y),
                    type,
                    name));
            }

            _baselineEstablished = true;
            return deaths;
        }
    }
}
