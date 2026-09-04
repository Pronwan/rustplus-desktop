using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesk.Services.Support;

/// <summary>One row in the user's ticket list.</summary>
public sealed record TicketSummary(
    string Id,
    string Category,
    string Status,
    string? Resolution,
    string Subject,
    bool HasUnread,
    DateTimeOffset? LastActivityAt);

/// <summary>A file hung off a ticket or one of its replies.</summary>
public sealed record TicketAttachment(string Id, string Name, long Size, string Mime);

/// <summary>One line in a ticket's thread.</summary>
public sealed record TicketMessage(
    string Id,
    string Kind,
    string Body,
    string? AuthorName,
    bool IsStaff,
    IReadOnlyList<TicketAttachment> Attachments,
    DateTimeOffset? CreatedAt);

/// <summary>The full ticket, thread included.</summary>
public sealed record TicketDetail(
    string Id,
    string Category,
    string Status,
    string? Resolution,
    string Subject,
    string Body,
    IReadOnlyList<TicketAttachment> Attachments,
    IReadOnlyList<TicketMessage> Messages,
    DateTimeOffset? CreatedAt);

/// <summary>One item in the notification centre.</summary>
public sealed record NotificationItem(
    string Id,
    string Type,
    string Level,
    string Title,
    string Body,
    string? Url,
    string? CtaLabel,
    bool Read,
    DateTimeOffset? CreatedAt);

/// <summary>An account's active mute, offered as something to appeal.</summary>
public sealed record AppealableSanction(string Id, string Kind, string Reason, DateTimeOffset? ExpiresAt);

/// <summary>What the new-ticket form needs: the categories and the current mute, if any.</summary>
public sealed record TicketMeta(IReadOnlyList<string> Categories, AppealableSanction? Appealable);

/// <summary>
/// Support tickets and the notification centre, from the client's side.
///
/// The mirror of the two API groups the backend exposes: a user files a ticket, reads the thread
/// and replies; and reads the one inbox that everything - a reply, an assignment, an announcement -
/// lands in. Attachments go up as multipart so a bug can carry its screenshots and logs.
/// </summary>
public static class SupportApi
{
    // ── Tickets ─────────────────────────────────────────────────────────────

    public static async Task<IReadOnlyList<TicketSummary>> GetTicketsAsync()
    {
        try
        {
            var body = await CloudApiClient.CallApiAsync("tickets", HttpMethod.Get).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return Array.Empty<TicketSummary>();

            return rows.EnumerateArray().Select(ParseSummary).ToList();
        }
        catch
        {
            return Array.Empty<TicketSummary>();
        }
    }

    public static async Task<TicketMeta> GetMetaAsync()
    {
        try
        {
            var body = await CloudApiClient.CallApiAsync("tickets/meta", HttpMethod.Get).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var categories = root.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array
                ? cats.EnumerateArray().Select(c => c.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();

            AppealableSanction? appeal = null;
            if (root.TryGetProperty("appealable_sanction", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                appeal = new AppealableSanction(
                    Str(s, "id") ?? "",
                    Str(s, "kind") ?? "timeout",
                    Str(s, "reason") ?? "",
                    Date(s, "expires_at"));
            }

            return new TicketMeta(categories, appeal);
        }
        catch
        {
            return new TicketMeta(Array.Empty<string>(), null);
        }
    }

    public static async Task<TicketDetail?> GetTicketAsync(string id)
    {
        try
        {
            var body = await CloudApiClient.CallApiAsync($"tickets/{id}", HttpMethod.Get).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("data", out var data) ? ParseDetail(data) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Files a ticket, with any attachments. Sent as multipart so a bug report can carry its
    /// screenshots, log and crash dump alongside the text. Returns whether it went through.
    /// </summary>
    public static async Task<bool> CreateTicketAsync(
        string category,
        string subject,
        string body,
        IReadOnlyDictionary<string, string>? meta = null,
        IReadOnlyList<string>? filePaths = null,
        string? sanctionId = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(category), "category" },
            { new StringContent(subject), "subject" },
            { new StringContent(body), "body" },
        };

        if (!string.IsNullOrEmpty(sanctionId))
            content.Add(new StringContent(sanctionId), "sanction_id");

        if (meta != null)
            foreach (var pair in meta)
                content.Add(new StringContent(pair.Value), $"meta[{pair.Key}]");

        AddFiles(content, filePaths);

        return await CloudApiClient.PostMultipartAsync("tickets", content).ConfigureAwait(false);
    }

    /// <summary>Adds a reply to a ticket, with any attachments. Multipart, same as filing one.</summary>
    public static async Task<bool> ReplyAsync(string ticketId, string body, IReadOnlyList<string>? filePaths = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(body), "body" },
        };
        AddFiles(content, filePaths);

        return await CloudApiClient.PostMultipartAsync($"tickets/{ticketId}/messages", content).ConfigureAwait(false);
    }

    /// <summary>The raw bytes of an attachment - for a thumbnail, or to stage before opening.</summary>
    public static Task<byte[]?> GetAttachmentBytesAsync(string ticketId, string mediaId)
        => CloudApiClient.GetBytesAsync($"tickets/{ticketId}/attachments/{mediaId}");

    private static string CacheDir => Path.Combine(Path.GetTempPath(), "rpd-tickets", "cache");

    /// <summary>
    /// Seeds the attachment cache from a file the client itself uploaded, so its thumbnail comes
    /// straight off disk and is never fetched back from the server. Keyed by the server's media id,
    /// so it also serves future sessions.
    /// </summary>
    public static void SeedAttachmentCache(string mediaId, string fileName, string localPath)
    {
        try
        {
            if (!File.Exists(localPath)) return;
            Directory.CreateDirectory(CacheDir);
            var safe = string.Join("_", ($"{mediaId}_{fileName}").Split(Path.GetInvalidFileNameChars()));
            var dest = Path.Combine(CacheDir, safe);
            if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
                File.Copy(localPath, dest, overwrite: true);
        }
        catch { /* best-effort: falls back to a server fetch */ }
    }

    /// <summary>
    /// Attachment bytes, saved locally on first fetch and served from that cache afterwards - so a
    /// thumbnail draws instantly on the next open and survives a dropped connection. Falls back to a
    /// plain fetch if the cache cannot be written.
    /// </summary>
    public static async Task<byte[]?> GetAttachmentCachedAsync(string ticketId, string mediaId, string fileName)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var safe = string.Join("_", ($"{mediaId}_{fileName}").Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(CacheDir, safe);

            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return await File.ReadAllBytesAsync(path).ConfigureAwait(false);

            var bytes = await GetAttachmentBytesAsync(ticketId, mediaId).ConfigureAwait(false);
            if (bytes is { Length: > 0 })
            {
                try { await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false); } catch { /* cache is best-effort */ }
            }
            return bytes;
        }
        catch
        {
            return await GetAttachmentBytesAsync(ticketId, mediaId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Downloads an attachment to a temp file and returns its path, so it can be opened in whatever
    /// the OS uses for that type. Null if it could not be fetched.
    /// </summary>
    public static async Task<string?> SaveAttachmentToTempAsync(string ticketId, string mediaId, string fileName)
    {
        // Cached path first, so opening a file the client uploaded never round-trips to the server.
        var bytes = await GetAttachmentCachedAsync(ticketId, mediaId, fileName).ConfigureAwait(false);
        if (bytes == null)
            return null;

        var safe = string.Join("_", (fileName ?? "attachment").Split(Path.GetInvalidFileNameChars()));
        var dir = Path.Combine(Path.GetTempPath(), "rpd-tickets");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, safe);
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        return path;
    }

    /// <summary>Clears this account's unread flag on a ticket.</summary>
    public static async Task MarkTicketReadAsync(string ticketId)
    {
        try
        {
            await CloudApiClient.CallApiAsync($"tickets/{ticketId}/read", HttpMethod.Post).ConfigureAwait(false);
        }
        catch { /* a read receipt that fails to send is not worth surfacing */ }
    }

    // ── Notifications ───────────────────────────────────────────────────────

    public static async Task<IReadOnlyList<NotificationItem>> GetNotificationsAsync()
    {
        try
        {
            var body = await CloudApiClient.CallApiAsync("notifications", HttpMethod.Get).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return Array.Empty<NotificationItem>();

            return rows.EnumerateArray().Select(ParseNotification).ToList();
        }
        catch
        {
            return Array.Empty<NotificationItem>();
        }
    }

    public static async Task<int> GetUnreadCountAsync()
    {
        try
        {
            var body = await CloudApiClient.CallApiAsync("notifications/unread-count", HttpMethod.Get).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("unread", out var u) && u.TryGetInt32(out var n) ? n : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static async Task MarkNotificationReadAsync(string id)
    {
        try { await CloudApiClient.CallApiAsync($"notifications/{id}/read", HttpMethod.Post).ConfigureAwait(false); }
        catch { }
    }

    public static async Task MarkAllNotificationsReadAsync()
    {
        try { await CloudApiClient.CallApiAsync("notifications/read-all", HttpMethod.Post).ConfigureAwait(false); }
        catch { }
    }

    /// <summary>Hides one notification for this account, keeping it in the database.</summary>
    public static async Task DismissNotificationAsync(string id)
    {
        try { await CloudApiClient.CallApiAsync($"notifications/{id}/dismiss", HttpMethod.Post).ConfigureAwait(false); }
        catch { }
    }

    /// <summary>Clears everything currently in the inbox; only newer notifications show afterwards.</summary>
    public static async Task ClearAllNotificationsAsync()
    {
        try { await CloudApiClient.CallApiAsync("notifications/clear-all", HttpMethod.Post).ConfigureAwait(false); }
        catch { }
    }

    // ── Parsing ─────────────────────────────────────────────────────────────

    private static void AddFiles(MultipartFormDataContent content, IReadOnlyList<string>? filePaths)
    {
        if (filePaths == null)
            return;

        foreach (var path in filePaths)
        {
            if (!File.Exists(path))
                continue;

            // Named "attachments[]" so Laravel reads it as the attachments array. Streamed rather
            // than read whole so a large crash dump does not sit in memory twice.
            var stream = File.OpenRead(path);
            content.Add(new StreamContent(stream), "attachments[]", Path.GetFileName(path));
        }
    }

    private static TicketSummary ParseSummary(JsonElement e) => new(
        Str(e, "id") ?? "",
        Str(e, "category") ?? "other",
        Str(e, "status") ?? "open",
        Str(e, "resolution"),
        Str(e, "subject") ?? "",
        e.TryGetProperty("has_unread", out var u) && u.ValueKind == JsonValueKind.True,
        Date(e, "last_activity_at"));

    private static TicketDetail ParseDetail(JsonElement e) => new(
        Str(e, "id") ?? "",
        Str(e, "category") ?? "other",
        Str(e, "status") ?? "open",
        Str(e, "resolution"),
        Str(e, "subject") ?? "",
        Str(e, "body") ?? "",
        ParseAttachments(e),
        e.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().Select(ParseMessage).ToList()
            : new List<TicketMessage>(),
        Date(e, "created_at"));

    private static TicketMessage ParseMessage(JsonElement e)
    {
        JsonElement author = default;
        var authorName = e.TryGetProperty("author", out author) && author.ValueKind == JsonValueKind.Object
            ? Str(author, "name")
            : null;

        return new TicketMessage(
            Str(e, "id") ?? "",
            Str(e, "kind") ?? "message",
            Str(e, "body") ?? "",
            authorName,
            e.TryGetProperty("is_staff", out var s) && s.ValueKind == JsonValueKind.True,
            ParseAttachments(e),
            Date(e, "created_at"));
    }

    private static IReadOnlyList<TicketAttachment> ParseAttachments(JsonElement e)
    {
        if (!e.TryGetProperty("attachments", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<TicketAttachment>();

        return arr.EnumerateArray().Select(a => new TicketAttachment(
            Str(a, "id") ?? "",
            Str(a, "name") ?? "file",
            a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) ? n : 0,
            Str(a, "mime") ?? "application/octet-stream")).ToList();
    }

    private static NotificationItem ParseNotification(JsonElement e) => new(
        Str(e, "id") ?? "",
        Str(e, "type") ?? "notification",
        Str(e, "level") ?? "info",
        Str(e, "title") ?? "",
        Str(e, "body") ?? "",
        Str(e, "url"),
        Str(e, "cta_label"),
        e.TryGetProperty("read_at", out var r) && r.ValueKind == JsonValueKind.String,
        Date(e, "created_at"));

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Date(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(v.GetString(), out var d) ? d : null;
}
