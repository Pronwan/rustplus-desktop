using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Infrastructure.Accounts;

/// <summary>Injects an Expo probe and correlates arrival on this profile source's own listener.</summary>
public sealed class CompanionPushHealth : ICompanionPushHealth
{
    internal const string TitleMarker = "rpd-selftest:";
    private readonly HttpClient http;
    private readonly ICompanionAccountService accounts;
    private readonly Func<Task, CancellationToken, Task<bool>> waitForArrival;
    private readonly ConcurrentDictionary<string, Pending> pending = new();

    public CompanionPushHealth(HttpClient http, ICompanionAccountService accounts)
        : this(http, accounts, async (arrival, ct) =>
        {
            try { await arrival.WaitAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false); return true; }
            catch (TimeoutException) { return false; }
        })
    { }

    internal CompanionPushHealth(HttpClient http, ICompanionAccountService accounts, Func<Task, CancellationToken, Task<bool>> waitForArrival)
    { this.http = http; this.accounts = accounts; this.waitForArrival = waitForArrival; }

    internal bool TryConsume(string profilesPath, string? title)
    {
        if (title?.StartsWith(TitleMarker, StringComparison.Ordinal) != true) return false;
        if (pending.TryGetValue(title, out var probe) && probe.Source == SourceKey(profilesPath)) probe.Arrival.TrySetResult();
        return true;
    }

    public async Task<PushHealthReport> CheckAsync(string profilesPath, CancellationToken cancellationToken)
    {
        string title = TitleMarker + Guid.NewGuid().ToString("N");
        var probe = new Pending(SourceKey(profilesPath));
        try
        {
            string configPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilesPath))!, "rustplusjs-config.json");
            var account = await accounts.CheckAsync(configPath, cancellationToken).ConfigureAwait(false);
            if (account.State is AccountState.Missing or AccountState.Expired or AccountState.Rejected)
                return new(PushHealthOutcome.SignInRequired, "Sign in with Steam to restore Rust+ notifications.");
            if (account.State != AccountState.Active) return Inconclusive();
            var config = await CompanionConfigFile.ReadAsync(configPath, cancellationToken).ConfigureAwait(false);
            string? token = config.Text("expo_push_token");
            if (string.IsNullOrWhiteSpace(token)) return new(PushHealthOutcome.Inconclusive, "No saved push token is available to test.");
            pending[title] = probe;
            using var sent = await http.PostAsJsonAsync("https://exp.host/--/api/v2/push/send", new
            {
                to = token,
                title,
                body = "Rust+ Desktop connection check",
                channelId = "rpd-selftest",
                priority = "high",
                ttl = 60,
                // Expo's data becomes the SDK's typed Body; an empty object remains decodable.
                data = new Dictionary<string, string>()
            }, cancellationToken).ConfigureAwait(false);
            if (!sent.IsSuccessStatusCode) return Inconclusive();
            using var sentJson = await ReadJsonAsync(sent, cancellationToken).ConfigureAwait(false);
            if (!sentJson.RootElement.TryGetProperty("data", out var ticket)) return Inconclusive();
            if (ticket.ValueKind == JsonValueKind.Array) ticket = ticket.GetArrayLength() == 1 ? ticket[0] : default;
            if (ticket.ValueKind != JsonValueKind.Object) return Inconclusive();
            if (Error(ticket) == "DeviceNotRegistered") return Rejected();
            if (Text(ticket, "status") != "ok" || Text(ticket, "id") is not { Length: > 0 } ticketId) return Inconclusive();
            if (await waitForArrival(probe.Arrival.Task, cancellationToken).ConfigureAwait(false)) return Healthy();
            using var receipt = await http.PostAsJsonAsync("https://exp.host/--/api/v2/push/getReceipts", new { ids = new[] { ticketId } }, cancellationToken).ConfigureAwait(false);
            if (probe.Arrival.Task.IsCompletedSuccessfully) return Healthy();
            if (!receipt.IsSuccessStatusCode) return Inconclusive();
            using var receiptJson = await ReadJsonAsync(receipt, cancellationToken).ConfigureAwait(false);
            if (!receiptJson.RootElement.TryGetProperty("data", out var receipts) || receipts.ValueKind != JsonValueKind.Object) return Inconclusive();
            if (receipts.TryGetProperty(ticketId, out var entry))
            {
                if (entry.ValueKind != JsonValueKind.Object) return Inconclusive();
                if (Error(entry) == "DeviceNotRegistered") return Rejected();
                if (Text(entry, "status") != "ok") return Inconclusive();
            }
            // A ticket or receipt acknowledges service handoff, not arrival at this listener.
            return new(PushHealthOutcome.NotDelivered, "The test push was accepted but did not arrive within the test window.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Inconclusive(); }
        finally { pending.TryRemove(title, out _); }
    }

    private static PushHealthReport Healthy() => new(PushHealthOutcome.Healthy, "This device received the test push. Push delivery is working.");
    private static PushHealthReport Rejected() => new(PushHealthOutcome.TokenRejected, "The saved push registration was rejected. It needs renewal.");
    private static PushHealthReport Inconclusive() => new(PushHealthOutcome.Inconclusive, "Push delivery could not be verified. Check your connection and try again.");
    private static string? Text(JsonElement node, string key)
        => node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? Error(JsonElement node)
        => node.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object ? Text(details, "error") : null;
    private static string SourceKey(string path) => OperatingSystem.IsWindows() ? Path.GetFullPath(path).ToUpperInvariant() : Path.GetFullPath(path);
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (body.Length > 65536) throw new JsonException();
        return JsonDocument.Parse(body);
    }
    private sealed class Pending(string source)
    {
        public string Source { get; } = source;
        public TaskCompletionSource Arrival { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
