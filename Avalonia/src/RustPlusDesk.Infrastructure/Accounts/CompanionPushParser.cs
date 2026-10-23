using System.Text.RegularExpressions;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Infrastructure.Accounts;

internal static class CompanionPushParser
{
    private static readonly Regex DeathTitle = new("^(?:You were killed by|Du wurdest getötet von)\\s+(?<attacker>.+)", RegexOptions.IgnoreCase);

    public static CompanionUpdate Parse(FcmMessage message, DateTimeOffset receivedAt)
    {
        var data = message.Data;
        var body = data.Body;
        string title = data.Title ?? "";
        var death = DeathTitle.Match(title);
        var kind = death.Success ? CompanionUpdateKind.Death : data.ChannelId?.ToLowerInvariant() switch
        {
            "pairing" => CompanionUpdateKind.Pairing,
            "alarm" => CompanionUpdateKind.Alarm,
            "chat" => CompanionUpdateKind.Chat,
            _ => CompanionUpdateKind.Notice
        };
        DateTimeOffset sent = message.SentAt == default ? receivedAt : new DateTimeOffset(message.SentAt.Kind switch
        {
            DateTimeKind.Local => message.SentAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(message.SentAt, DateTimeKind.Utc)
        });
        if (receivedAt - sent < TimeSpan.FromMinutes(-5) || receivedAt - sent > TimeSpan.FromDays(30)) sent = receivedAt;
        string text = kind == CompanionUpdateKind.Death ? death.Groups["attacker"].Value.Trim().Trim('\'', '"')
            : data.Message ?? "";
        return new(kind, Limit(title), Limit(text))
        {
            ServerName = Limit(body?.Name ?? ""),
            Host = Limit(body?.Ip ?? ""),
            Port = body?.Port is > 0 and <= 65535 ? body.Port : null,
            EntityId = body?.EntityId is > 0 and <= uint.MaxValue ? (uint)body.EntityId : null,
            OccurredAt = sent,
            ReceivedAt = receivedAt
        };
    }

    private static string Limit(string text) => text.Length > 4096 ? text[..4096] : text;
}
