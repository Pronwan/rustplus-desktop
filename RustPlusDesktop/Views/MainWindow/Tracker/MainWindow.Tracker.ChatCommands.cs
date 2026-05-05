using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>Per-(author, command) cooldown to suppress accidental spam.</summary>
    private readonly Dictionary<(string author, string cmd), DateTime> _chatCmdCooldown = new();
    private static readonly TimeSpan _chatCmdCooldownSpan = TimeSpan.FromSeconds(5);

    /// <summary>~120 chars/line keeps us under Rust's team-chat per-message ceiling.</summary>
    private const int ChatLineMaxChars = 120;

    /// <summary>
    /// Inspect an inbound team-chat message; if it's a !track* command, reply in team chat.
    /// Returns true if the message was handled (so callers can short-circuit).
    /// </summary>
    private bool TryHandleTrackChatCommand(string author, string text)
    {
        if (!TrackingService.EnableChatCommands) return false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        // Everything we react to begins with "!track" or "!trackgroups" / "!groups".
        if (!trimmed.StartsWith("!track", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Equals("!groups", StringComparison.OrdinalIgnoreCase))
            return false;

        // Per-author + per-command cooldown
        string cooldownKey = (trimmed.Length > 32 ? trimmed.Substring(0, 32) : trimmed).ToLowerInvariant();
        var key = (author ?? "", cooldownKey);
        if (_chatCmdCooldown.TryGetValue(key, out var last) &&
            (DateTime.UtcNow - last) < _chatCmdCooldownSpan)
        {
            return true; // swallow silently — we've already responded recently
        }
        _chatCmdCooldown[key] = DateTime.UtcNow;

        // Dispatch
        var lower = trimmed.ToLowerInvariant();
        List<string> reply;

        if (lower.Equals("!track") || lower.Equals("!track help"))
        {
            reply = lower.EndsWith("help") ? BuildTrackHelpResponse() : BuildTrackResponse();
        }
        else if (lower.Equals("!trackteam") || lower.Equals("!trackgroups") || lower.Equals("!groups"))
        {
            reply = BuildTrackTeamResponse();
        }
        else
        {
            // !track <name>  OR  !track<name>  (no space)
            string groupName;
            if (lower.StartsWith("!track ") && trimmed.Length > 7)
                groupName = trimmed.Substring(7).Trim();
            else if (lower.StartsWith("!track") && trimmed.Length > 6)
                groupName = trimmed.Substring(6).Trim();
            else
                return false;

            if (string.IsNullOrEmpty(groupName)) reply = BuildTrackHelpResponse();
            else reply = BuildTrackGroupResponse(groupName);
        }

        _ = SendChatLinesAsync(reply);
        return true;
    }

    // ─── Response builders ───────────────────────────────────────────────────

    private List<string> BuildTrackHelpResponse()
    {
        return new List<string>
        {
            "[track] !track all · !trackteam groups · !track<name> group members"
        };
    }

    private List<string> BuildTrackResponse()
    {
        var tracked = TrackingService.GetTrackedPlayers();
        var onlineByBMId = TrackingService.LastOnlinePlayers.ToDictionary(p => p.BMId, p => p);

        if (tracked.Count == 0)
            return new List<string> { "[track] No tracked players." };

        var online = new List<string>();
        var offline = new List<string>();

        foreach (var t in tracked.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (onlineByBMId.TryGetValue(t.BMId, out var live))
            {
                online.Add($"{t.Name} ({live.PlayTimeStr})");
            }
            else
            {
                var lastSession = t.Sessions != null && t.Sessions.Count > 0
                    ? t.Sessions[t.Sessions.Count - 1] : null;
                string suffix = lastSession?.DisconnectTime is DateTime d
                    ? $" ({RelativeTime(d)})"
                    : "";
                offline.Add($"{t.Name}{suffix}");
            }
        }

        var lines = new List<string> { $"[track] {tracked.Count} tracked · {online.Count} online" };
        if (online.Count > 0) AppendTaggedList(lines, "ONLINE", online);
        if (offline.Count > 0) AppendTaggedList(lines, "OFFLINE", offline);
        return lines;
    }

    private List<string> BuildTrackTeamResponse()
    {
        var groups = PlayerGroupsService.Groups;
        if (groups.Count == 0)
            return new List<string> { "[track] No groups defined." };

        var onlineSet = TrackingService.LastOnlinePlayers
            .Select(p => p.BMId).ToHashSet();

        var parts = groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                int onCount = g.BMIds.Count(id => onlineSet.Contains(id));
                return $"{g.Name} ({onCount}/{g.BMIds.Count})";
            })
            .ToList();

        var lines = new List<string> { "[track] Groups:" };
        AppendTaggedList(lines, "", parts, prefixFirstLineWithTag: false);
        return lines;
    }

    private List<string> BuildTrackGroupResponse(string groupName)
    {
        var match = PlayerGroupsService.Groups
            .FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return new List<string> { $"[track] No group named \"{groupName}\"." };

        var trackedByBMId = TrackingService.GetTrackedPlayers().ToDictionary(p => p.BMId, p => p);
        var onlineByBMId = TrackingService.LastOnlinePlayers.ToDictionary(p => p.BMId, p => p);

        var online = new List<string>();
        var offline = new List<string>();

        foreach (var bmId in match.BMIds)
        {
            string name = onlineByBMId.TryGetValue(bmId, out var o) ? o.Name
                : trackedByBMId.TryGetValue(bmId, out var t) ? t.Name
                : "(unknown)";

            if (onlineByBMId.TryGetValue(bmId, out var live))
            {
                online.Add($"{name} ({live.PlayTimeStr})");
            }
            else
            {
                var tp = trackedByBMId.TryGetValue(bmId, out var tt) ? tt : null;
                var lastSession = tp?.Sessions != null && tp.Sessions.Count > 0
                    ? tp.Sessions[tp.Sessions.Count - 1] : null;
                string suffix = lastSession?.DisconnectTime is DateTime d
                    ? $" ({RelativeTime(d)})"
                    : "";
                offline.Add($"{name}{suffix}");
            }
        }

        var lines = new List<string> { $"[track] {match.Name}: {online.Count}/{match.BMIds.Count} online" };
        if (online.Count > 0) AppendTaggedList(lines, "ONLINE", online);
        if (offline.Count > 0) AppendTaggedList(lines, "OFFLINE", offline);
        return lines;
    }

    // ─── Chunking + send ─────────────────────────────────────────────────────

    /// <summary>
    /// Append a comma-separated list (e.g. "ONLINE: a, b, c") to <paramref name="lines"/>,
    /// wrapping as needed. Compact mode caps at 2 wrapped lines per tag and ends with "+N more".
    /// </summary>
    private static void AppendTaggedList(
        List<string> lines, string tag, List<string> items,
        bool prefixFirstLineWithTag = true,
        int maxWrappedLines = 2)
    {
        if (items.Count == 0) return;

        string prefix = prefixFirstLineWithTag && !string.IsNullOrEmpty(tag) ? $"{tag}: " : "";
        var current = prefix;
        int wrappedSoFar = 0;
        int rendered = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var sep = current.EndsWith(": ") || current.Length == 0 ? "" : ", ";
            var candidate = current + sep + items[i];

            if (candidate.Length > ChatLineMaxChars)
            {
                if (current.Length > 0 && current != prefix)
                {
                    lines.Add(current.TrimEnd(',', ' '));
                    wrappedSoFar++;
                    rendered = i;
                }
                if (wrappedSoFar >= maxWrappedLines)
                {
                    int remaining = items.Count - rendered;
                    if (remaining > 0)
                    {
                        // Replace the last appended line's tail with "+N more".
                        var lastIdx = lines.Count - 1;
                        if (lastIdx >= 0)
                            lines[lastIdx] = TrimWithMore(lines[lastIdx], remaining);
                    }
                    return;
                }
                current = items[i];
            }
            else
            {
                current = candidate;
            }
        }

        if (!string.IsNullOrEmpty(current) && current != prefix)
            lines.Add(current);
    }

    private static string TrimWithMore(string line, int remaining)
    {
        var marker = $" +{remaining} more";
        if (line.Length + marker.Length <= ChatLineMaxChars) return line + marker;
        // Trim list to fit the marker.
        var available = ChatLineMaxChars - marker.Length;
        if (available <= 0) return marker.TrimStart();
        var trimmed = line.Length > available ? line.Substring(0, available) : line;
        // Don't slice mid-name — drop trailing partial token if any.
        int lastComma = trimmed.LastIndexOf(',');
        if (lastComma > 0) trimmed = trimmed.Substring(0, lastComma);
        return trimmed + marker;
    }

    private async Task SendChatLinesAsync(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { await SendTeamChatSafeAsync(line); }
            catch (Exception ex) { AppendLog($"[chat-cmd] send failed: {ex.Message}"); }
            // brief gap so the lines arrive in order and aren't merged or rate-limited
            await Task.Delay(250);
        }
    }
}
