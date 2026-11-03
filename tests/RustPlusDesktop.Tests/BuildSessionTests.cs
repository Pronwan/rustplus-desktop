using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using RustPlusDesk.Services.Builder;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

/// <summary>
/// Co-op Base Builder on the client: reading what the platform says about a session, and routing
/// its broadcasts. The account channel and the session channel both carry builder.session_ended,
/// and they mean different things.
/// </summary>
[TestClass]
public sealed class BuildSessionTests
{
    private const string SessionId = "9b2f3c1e-1111-4a5b-8c9d-000000000001";

    [TestMethod]
    public void ReadsTheHostsViewOfASession()
    {
        var session = BuildSessionInfo.Parse(JObject.Parse($$"""
            {
              "id": "{{SessionId}}", "status": "active", "base_name": "Bunker", "role": "host",
              "can_build": true, "epoch": 2, "channel": "build-sessions.{{SessionId}}.2",
              "host": { "id": "u1", "display_name": "Carsten", "avatar_url": null, "steam_id": "76561190000000001" },
              "members": [ { "id": "m1", "status": "joined", "can_build": false,
                             "user": { "id": "u2", "display_name": "Jawad", "steam_id": "76561190000000002" } } ],
              "invite_expires_at": null
            }
            """));

        Assert.IsNotNull(session);
        Assert.IsTrue(session.IsHost);
        Assert.IsTrue(session.IsActive);
        Assert.AreEqual(2, session.Epoch);
        Assert.AreEqual($"build-sessions.{SessionId}.2", session.Channel);
        Assert.AreEqual("Jawad", session.Members[0].User?.DisplayName);
        Assert.IsFalse(session.Members[0].CanBuild);
    }

    [TestMethod]
    public void AnInviteeHasNoChannelYet()
    {
        var session = BuildSessionInfo.Parse(JObject.Parse($$"""
            { "id": "{{SessionId}}", "status": "active", "role": "guest", "can_build": false,
              "epoch": null, "channel": null, "members": [], "invite_expires_at": "2026-11-03T10:02:00+00:00" }
            """));

        Assert.IsNotNull(session);
        Assert.IsFalse(session.IsHost);
        Assert.IsNull(session.Epoch);
        Assert.IsNull(session.Channel);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 3, 10, 2, 0, TimeSpan.Zero), session.InviteExpiresAt);
    }

    [TestMethod]
    public void ReadsMissedInvitesFromTheCurrentState()
    {
        var state = BuildSessionsState.Parse($$"""
            { "data": { "hosting": null, "joined": null, "invites": [
              { "id": "{{SessionId}}", "status": "active", "base_name": "Bunker", "role": "guest",
                "host": { "id": "u1", "display_name": "Carsten" }, "members": [],
                "invite_expires_at": "2026-11-03T10:02:00Z" } ] } }
            """);

        Assert.IsNull(state.Hosting);
        Assert.AreEqual(1, state.Invites.Count);
        Assert.AreEqual("Bunker", state.Invites[0].BaseName);
        Assert.AreEqual("Carsten", state.Invites[0].Host?.DisplayName);
    }

    [TestMethod]
    public void RaisesAnInviteFromTheAccountChannel()
    {
        BuildInvite? received = null;
        void Handler(BuildInvite i) => received = i;
        BuildSessionRealtime.InviteReceived += Handler;
        try
        {
            BuildSessionRealtime.OnEvent("private-users.u2", ".builder.invite", JObject.Parse($$"""
                { "session_id": "{{SessionId}}", "base_name": "Bunker",
                  "host": { "id": "u1", "display_name": "Carsten" }, "expires_at": "2026-11-03T10:02:00Z" }
                """));
        }
        finally { BuildSessionRealtime.InviteReceived -= Handler; }

        Assert.IsNotNull(received);
        Assert.AreEqual(SessionId, received.SessionId);
        Assert.AreEqual("Carsten", received.Host?.DisplayName);
    }

    [TestMethod]
    public void AnEndedSessionWithdrawsAnInviteOnTheAccountChannelButEndsItOnTheSessionChannel()
    {
        string? withdrawn = null, ended = null, reason = null;
        void OnWithdrawn(string id) => withdrawn = id;
        void OnEnded(string id, string? r) { ended = id; reason = r; }
        BuildSessionRealtime.InviteWithdrawn += OnWithdrawn;
        BuildSessionRealtime.SessionEnded += OnEnded;
        try
        {
            var data = JObject.Parse($$"""{ "session_id": "{{SessionId}}", "reason": "host_ended" }""");

            BuildSessionRealtime.OnEvent("private-users.u3", "builder.session_ended", data);
            Assert.AreEqual(SessionId, withdrawn);
            Assert.IsNull(ended);

            BuildSessionRealtime.OnEvent($"private-build-sessions.{SessionId}.1", "builder.session_ended", data);
            Assert.AreEqual(SessionId, ended);
            Assert.AreEqual("host_ended", reason);
        }
        finally
        {
            BuildSessionRealtime.InviteWithdrawn -= OnWithdrawn;
            BuildSessionRealtime.SessionEnded -= OnEnded;
        }
    }

    [TestMethod]
    public void IgnoresOtherEventsAndPayloadsWithoutASession()
    {
        var raised = false;
        void Handler(BuildInvite _) => raised = true;
        BuildSessionRealtime.InviteReceived += Handler;
        try
        {
            BuildSessionRealtime.OnEvent("private-users.u2", "social.friend_request", new JObject { ["session_id"] = SessionId });
            BuildSessionRealtime.OnEvent("private-users.u2", "builder.invite", new JObject());
        }
        finally { BuildSessionRealtime.InviteReceived -= Handler; }

        Assert.IsFalse(raised);
    }

    [TestMethod]
    public void StaysOffUntilTheFlagIsOnTheFeed()
    {
        CloudFeatureFlags.Load("{\"data\":{\"alexa\":{\"enabled\":true}}}");
        Assert.IsFalse(CloudFeatureFlags.IsWithinWindow(BuildSessionApi.FeatureFlag));

        CloudFeatureFlags.Load("{\"data\":{\"coop_builder\":{\"enabled\":false}}}");
        Assert.IsFalse(CloudFeatureFlags.IsWithinWindow(BuildSessionApi.FeatureFlag));

        CloudFeatureFlags.Load("{\"data\":{\"coop_builder\":{\"enabled\":true}}}");
        Assert.IsTrue(CloudFeatureFlags.IsWithinWindow(BuildSessionApi.FeatureFlag));
    }
}
