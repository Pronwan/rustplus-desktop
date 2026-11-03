using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RustPlusDesk.Services.Builder;

/// <summary>Someone in a co-op build session, as the platform describes them.</summary>
public sealed record BuildSessionPerson(string Id, string DisplayName, string? AvatarUrl, string? SteamId)
{
    internal static BuildSessionPerson? Parse(JToken? token)
    {
        if (token is not JObject o) return null;
        var id = o["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return null;

        return new BuildSessionPerson(
            id,
            o["display_name"]?.ToString() is { Length: > 0 } name ? name : "—",
            o["avatar_url"]?.ToString(),
            o["steam_id"]?.ToString());
    }
}

public sealed record BuildSessionMember(string Id, string Status, bool CanBuild, BuildSessionPerson? User);

/// <summary>
/// One session from the viewer's side. <see cref="Channel"/> is only set for the host and for
/// guests who joined - an invitee who has not answered has nothing to subscribe to yet.
/// </summary>
public sealed record BuildSessionInfo(
    string Id,
    string Status,
    string? BaseName,
    bool IsHost,
    bool CanBuild,
    int? Epoch,
    string? Channel,
    BuildSessionPerson? Host,
    IReadOnlyList<BuildSessionMember> Members,
    DateTimeOffset? InviteExpiresAt)
{
    public bool IsActive => Status == "active";

    internal static BuildSessionInfo? Parse(JToken? token)
    {
        if (token is not JObject o) return null;
        var id = o["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return null;

        var members = (o["members"] as JArray ?? new JArray())
            .OfType<JObject>()
            .Select(m => new BuildSessionMember(
                m["id"]?.ToString() ?? "",
                m["status"]?.ToString() ?? "",
                m["can_build"]?.Value<bool>() == true,
                BuildSessionPerson.Parse(m["user"])))
            .ToList();

        return new BuildSessionInfo(
            id,
            o["status"]?.ToString() ?? "",
            o["base_name"]?.ToString(),
            o["role"]?.ToString() == "host",
            o["can_build"]?.Value<bool>() == true,
            o["epoch"]?.Type == JTokenType.Integer ? o["epoch"]!.Value<int>() : null,
            o["channel"]?.Type == JTokenType.String ? o["channel"]!.ToString() : null,
            BuildSessionPerson.Parse(o["host"]),
            members,
            ParseTime(o["invite_expires_at"]));
    }

    /// <summary>
    /// Newtonsoft turns an ISO timestamp into a local DateTime while parsing, and that DateTime's
    /// ToString() is in the user's culture - read back as text, a German "03.11." became March 11.
    /// So the parsed value is taken as it is, and only a string is parsed.
    /// </summary>
    internal static DateTimeOffset? ParseTime(JToken? token) => (token as JValue)?.Value switch
    {
        DateTimeOffset offset => offset,
        DateTime { Kind: DateTimeKind.Unspecified } unspecified => new DateTimeOffset(unspecified, TimeSpan.Zero),
        DateTime dateTime => new DateTimeOffset(dateTime.ToUniversalTime(), TimeSpan.Zero),
        string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
        _ => null,
    };
}

/// <summary>An invite waiting on this account, from the broadcast or from <c>build-sessions/current</c>.</summary>
public sealed record BuildInvite(string SessionId, string? BaseName, BuildSessionPerson? Host, DateTimeOffset? ExpiresAt)
{
    /// <summary>The <c>builder.invite</c> broadcast.</summary>
    internal static BuildInvite? FromBroadcast(JObject data)
    {
        var id = data["session_id"]?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return null;

        return new BuildInvite(id, data["base_name"]?.ToString(), BuildSessionPerson.Parse(data["host"]),
            BuildSessionInfo.ParseTime(data["expires_at"]));
    }

    internal static BuildInvite FromSession(BuildSessionInfo session) =>
        new(session.Id, session.BaseName, session.Host, session.InviteExpiresAt);
}

/// <summary>What <c>build-sessions/current</c> answers: the app's whole co-op state after a start.</summary>
public sealed record BuildSessionsState(BuildSessionInfo? Hosting, BuildSessionInfo? Joined, IReadOnlyList<BuildInvite> Invites)
{
    internal static BuildSessionsState Parse(string body)
    {
        var data = JObject.Parse(body)["data"] as JObject ?? new JObject();

        return new BuildSessionsState(
            BuildSessionInfo.Parse(data["hosting"]),
            BuildSessionInfo.Parse(data["joined"]),
            (data["invites"] as JArray ?? new JArray())
                .Select(BuildSessionInfo.Parse)
                .Where(s => s != null)
                .Select(s => BuildInvite.FromSession(s!))
                .ToList());
    }
}
