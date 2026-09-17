using System;
using System.Collections.Generic;
using System.Linq;

namespace RustPlusDesk.Services.Achievements;

/// <summary>
/// Tracks which achievements have been earned and when.
///
/// Everything is local. Unlocking is a fire-and-forget call from wherever the
/// thing actually happens, so <see cref="Unlock"/> has to be cheap and safe to
/// call repeatedly - most triggers sit in code that runs on every poll or every
/// redraw, and the second call onwards does nothing but a dictionary lookup.
/// </summary>
public static class AchievementService
{
    private const string CacheKey = "achievements";

    /// <summary>What gets written to disk.</summary>
    public sealed class AchievementState
    {
        /// <summary>Achievement id to when it was earned, UTC.</summary>
        public Dictionary<string, DateTime> Earned { get; set; } = new();

        /// <summary>Earned but not yet looked at - drives the badge on the button.</summary>
        public List<string> Unseen { get; set; } = new();
    }

    private static AchievementState? _state;
    private static readonly object Gate = new();

    /// <summary>Raised when something is earned, for the snackbar and the badge.</summary>
    public static event Action<AchievementDef>? Unlocked;

    /// <summary>Raised when the unseen count changes, in either direction.</summary>
    public static event Action<int>? UnseenChanged;

    private static AchievementState State
    {
        get
        {
            lock (Gate)
            {
                _state ??= StorageService.LoadCache<AchievementState>(CacheKey) ?? new AchievementState();
                return _state;
            }
        }
    }

    public static int EarnedCount => State.Earned.Count;

    public static int TotalCount => AchievementCatalog.All.Count;

    public static int UnseenCount => State.Unseen.Count;

    public static bool IsEarned(string id) => State.Earned.ContainsKey(id);

    public static DateTime? EarnedAt(string id)
        => State.Earned.TryGetValue(id, out var when) ? when : null;

    /// <summary>
    /// Marks an achievement as earned. Does nothing if it already is, so callers
    /// can sit in hot paths without checking first.
    /// </summary>
    public static void Unlock(string id)
    {
        AchievementDef? def;
        bool isNew;

        lock (Gate)
        {
            var state = State;
            if (state.Earned.ContainsKey(id)) return;

            def = AchievementCatalog.Find(id);
            if (def == null) return;   // unknown id: a typo should not write junk to disk

            state.Earned[id] = DateTime.UtcNow;
            if (!state.Unseen.Contains(id)) state.Unseen.Add(id);
            isNew = true;

            Save(state);
        }

        if (!isNew || def == null) return;

        Unlocked?.Invoke(def);
        UnseenChanged?.Invoke(UnseenCount);
    }

    /// <summary>Clears the badge. Called when the list has actually been looked at.</summary>
    public static void MarkAllSeen()
    {
        lock (Gate)
        {
            var state = State;
            if (state.Unseen.Count == 0) return;
            state.Unseen.Clear();
            Save(state);
        }

        UnseenChanged?.Invoke(0);
    }

    /// <summary>True once every achievement is earned.</summary>
    public static bool AllEarned => EarnedCount >= TotalCount;

    private static void Save(AchievementState state)
    {
        try
        {
            StorageService.SaveCache(CacheKey, state);
        }
        catch
        {
            // A failed write must never break whatever the player was doing; the
            // achievement stays unlocked for this session and is retried next time.
        }
    }
}
