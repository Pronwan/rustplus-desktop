using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// Global, admin-controlled feature flags read from the public
    /// <c>/api/v1/feature-flags</c> feed: whether a feature is enabled for all
    /// clients, and an optional status note to surface to the user.
    ///
    /// Fail-open: if the feed is unreachable or a key is unknown, the feature is
    /// treated as enabled with no note, so a transient outage never blocks the UI.
    /// </summary>
    public static class CloudFeatureFlags
    {
        public sealed record FlagState(bool Enabled, string? StatusNote);

        private static Dictionary<string, FlagState> _flags = new();

        /// <summary>Fetch the latest flags. Keeps the last known set on failure.</summary>
        public static async Task RefreshAsync()
        {
            var body = await CloudApiClient.GetPublicAsync("feature-flags");
            if (body == null) return;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    return;

                var next = new Dictionary<string, FlagState>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in data.EnumerateObject())
                {
                    var el = prop.Value;

                    // Enabled unless explicitly false.
                    var enabled = !el.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
                    var note = el.TryGetProperty("status_note", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()
                        : null;

                    next[prop.Name] = new FlagState(enabled, note);
                }

                _flags = next;
            }
            catch
            {
                // Malformed payload — keep the previous set.
            }
        }

        /// <summary>True unless an admin has explicitly disabled this feature.</summary>
        public static bool IsEnabled(string key) =>
            !_flags.TryGetValue(key, out var f) || f.Enabled;

        /// <summary>The admin status note for a feature, or null when none is set.</summary>
        public static string? Note(string key) =>
            _flags.TryGetValue(key, out var f) ? f.StatusNote : null;
    }
}
