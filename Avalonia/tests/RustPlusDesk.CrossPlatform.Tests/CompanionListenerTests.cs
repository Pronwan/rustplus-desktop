using System.Net.Sockets;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Infrastructure.Accounts;

namespace RustPlusDesk.CrossPlatform.Tests;

[TestClass]
public sealed class CompanionListenerTests
{
    [TestMethod]
    public async Task Pairing_SavesBeforeEmittingSafeUpdate_DeduplicatesAndPersistsIds()
    {
        using var files = Configured();
        var connections = new List<Connection>();
        var listener = Service(connections);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var stream = listener.ListenAsync(files.Profiles, stop.Token).GetAsyncEnumerator())
        {
            Assert.AreEqual(CompanionUpdateKind.Connecting, (await Next(stream)).Kind);
            Assert.AreEqual(CompanionUpdateKind.Listening, (await Next(stream)).Kind);
            var socket = connections.Single();
            socket.Send(Message("pair-1", "pairing"));
            socket.Send(Message("pair-1", "pairing"));
            socket.Send(Message("pair-redelivery", "pairing"));
            socket.Send(Message("chat-1", "chat"));
            var paired = await Next(stream);
            Assert.AreEqual(CompanionUpdateKind.Pairing, paired.Kind);
            Assert.IsTrue(File.Exists(files.Profiles), "Profile must be committed before UI success.");
            Assert.IsNotNull(paired.ProfileKey);
            Assert.IsFalse(JsonSerializer.Serialize(paired).Contains("PlayerToken", StringComparison.Ordinal));
            Assert.AreEqual(CompanionUpdateKind.Chat, (await Next(stream)).Kind);
        }
        string statePath = Directory.GetFiles(files.Directory, "fcm-persistent-ids.*.json").Single();
        StringAssert.Contains(await File.ReadAllTextAsync(statePath), "pair-1");
        Assert.IsTrue(connections[0].Disposed);
        Assert.AreEqual(0, connections[0].Subscribers);
    }

    [TestMethod]
    public async Task Reconnect_CoalescesDisconnectSignals_DisposesBeforeRetry_AndUsesPersistedIds()
    {
        using var files = Configured();
        await File.WriteAllTextAsync(Path.Combine(files.Directory, "fcm-persistent-ids.json"), "[\"old-1\"]");
        var connections = new List<Connection>();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = Service(connections, async (delay, ct) =>
        {
            Assert.AreEqual(TimeSpan.FromSeconds(3), delay);
            Assert.IsTrue(connections[0].Disposed);
            waiting.TrySetResult();
            await resume.Task.WaitAsync(ct);
        });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = listener.ListenAsync(files.Profiles, stop.Token).GetAsyncEnumerator();
        await Next(stream); await Next(stream);
        Assert.AreEqual("old-1", connections[0].Seed.Single());
        connections[0].Drop();
        Assert.AreEqual(CompanionUpdateKind.Reconnecting, (await Next(stream)).Kind);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(1, connections.Count);
        resume.TrySetResult();
        await Next(stream);
        Assert.AreEqual(CompanionUpdateKind.Listening, (await Next(stream)).Kind);
        Assert.AreEqual(2, connections.Count);
        connections[0].Send(Message("old-callback", "chat"));
        connections[1].Send(Message("fresh-callback", "chat"));
        Assert.AreEqual("fresh-callback", (await Next(stream)).Message);
    }

    [TestMethod]
    public async Task ConnectFailure_RetriesWithoutAbandoningPreviousConnection()
    {
        using var files = Configured();
        var connections = new List<Connection>();
        var listener = new CompanionListener((credentials, ids, receive) =>
        {
            var socket = new Connection(ids, receive) { ConnectError = connections.Count == 0 ? new SocketException() : null };
            connections.Add(socket);
            return socket;
        }, (_, _) => Task.CompletedTask);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = listener.ListenAsync(files.Profiles, stop.Token).GetAsyncEnumerator();
        await Next(stream); await Next(stream); await Next(stream);
        Assert.AreEqual(CompanionUpdateKind.Listening, (await Next(stream)).Kind);
        Assert.AreEqual(2, connections.Count);
        Assert.IsTrue(connections[0].Disposed);
    }

    [TestMethod]
    public async Task ParseError_DoesNotReconnect_AndInvalidPairingDoesNotPoisonNextPush()
    {
        using var files = Configured();
        var connections = new List<Connection>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = Service(connections).ListenAsync(files.Profiles, stop.Token).GetAsyncEnumerator();
        await Next(stream); await Next(stream);
        connections[0].ParseError();
        Assert.AreEqual(CompanionUpdateKind.Notice, (await Next(stream)).Kind);
        connections[0].Send(Message("invalid-1", "pairing") with { Data = new MessageData { ChannelId = "pairing", Body = PairingWriterTests.Pairing with { PlayerId = 0 } } });
        Assert.AreEqual("Pairing not saved", (await Next(stream)).Title);
        connections[0].Send(Message("valid-1", "pairing"));
        Assert.AreEqual(CompanionUpdateKind.Pairing, (await Next(stream)).Kind);
        Assert.AreEqual(1, connections.Count);
    }

    [TestMethod]
    public async Task MissingOrInvalidCredentials_FailSafelyWithoutOpeningSocket()
    {
        using var files = new PairingFiles();
        var connections = new List<Connection>();
        var service = Service(connections);
        await using (var stream = service.ListenAsync(files.Profiles, CancellationToken.None).GetAsyncEnumerator())
        {
            var error = await Assert.ThrowsExceptionAsync<CompanionListenerException>(async () => await stream.MoveNextAsync());
            Assert.AreEqual(CompanionListenerFailure.MissingCredentials, error.Failure);
        }
        await File.WriteAllTextAsync(files.Config, "{\"fcm_credentials\":{\"gcm\":{\"androidId\":\"invalid\",\"securityToken\":\"private-fixture\"}}}");
        await using (var stream = service.ListenAsync(files.Profiles, CancellationToken.None).GetAsyncEnumerator())
        {
            var error = await Assert.ThrowsExceptionAsync<CompanionListenerException>(async () => await stream.MoveNextAsync());
            Assert.AreEqual(CompanionListenerFailure.InvalidCredentials, error.Failure);
            Assert.IsFalse(error.ToString().Contains("private-fixture", StringComparison.Ordinal));
        }
        Assert.AreEqual(0, connections.Count);
    }

    [TestMethod]
    public async Task FailedProfileWrite_StopsListenerWithoutClaimingPairingSuccessOrRememberingId()
    {
        using var files = Configured();
        Directory.CreateDirectory(files.Profiles);
        var connections = new List<Connection>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = Service(connections).ListenAsync(files.Profiles, stop.Token).GetAsyncEnumerator();
        await Next(stream); await Next(stream);
        connections[0].Send(Message("must-retry", "pairing"));
        var error = await Assert.ThrowsExceptionAsync<CompanionListenerException>(async () => await stream.MoveNextAsync());
        Assert.AreEqual(CompanionListenerFailure.StorageUnavailable, error.Failure);
        Assert.IsTrue(connections[0].Disposed);
        Assert.AreEqual(0, Directory.GetFiles(files.Directory, "fcm-persistent-ids.*.json").Length);
    }

    [TestMethod]
    public void PushParser_PreservesSentTime_AndClassifiesDeathBeforeChannel()
    {
        var received = DateTimeOffset.UtcNow;
        var message = Message("death-1", "alarm") with
        { SentAt = received.AddHours(-2).UtcDateTime, Data = new MessageData { ChannelId = "alarm", Title = "You were killed by 'Fixture'", Body = PairingWriterTests.Pairing } };
        var death = CompanionPushParser.Parse(message, received);
        Assert.AreEqual(CompanionUpdateKind.Death, death.Kind);
        Assert.AreEqual("Fixture", death.Message);
        Assert.AreEqual(received.AddHours(-2), death.OccurredAt);
        Assert.AreEqual(received, CompanionPushParser.Parse(message with { SentAt = received.AddDays(1).UtcDateTime }, received).OccurredAt);
        Assert.AreEqual(received, CompanionPushParser.Parse(message with { SentAt = received.AddDays(-31).UtcDateTime }, received).OccurredAt);
    }

    [TestMethod]
    public async Task RePair_WithRotatedToken_IsNotSuppressedAsDuplicate()
    {
        using var files = Configured();
        var connections = new List<Connection>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = Service(connections).ListenAsync(files.Profiles, stop.Token).GetAsyncEnumerator();
        await Next(stream); await Next(stream);
        connections[0].Send(Message("pair-first", "pairing"));
        Assert.AreEqual(CompanionUpdateKind.Pairing, (await Next(stream)).Kind);
        connections[0].Send(Message("pair-rotated", "pairing") with
        { Data = new MessageData { ChannelId = "pairing", Body = PairingWriterTests.Pairing with { PlayerToken = "54321" } } });
        Assert.AreEqual(CompanionUpdateKind.Pairing, (await Next(stream)).Kind);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(files.Profiles));
        Assert.AreEqual("54321", saved.RootElement[0].GetProperty("PlayerToken").GetString());
    }

    internal static async Task<CompanionUpdate> Next(IAsyncEnumerator<CompanionUpdate> stream)
    {
        Assert.IsTrue(await stream.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4)));
        return stream.Current;
    }

    [TestMethod]
    public async Task HealthProbe_IsConsumedBeforePairingOrNotificationPresentation()
    {
        using var files = Configured();
        await File.WriteAllTextAsync(files.Config, "{\"expo_push_token\":\"fixture-expo\",\"fcm_credentials\":{\"gcm\":{\"androidId\":42,\"securityToken\":84}}}");
        Connection? connection = null;
        using var http = new HttpClient(new ProbeHandler(async (request, ct) =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            string title = json.RootElement.GetProperty("title").GetString()!;
            connection!.Send(new FcmMessage { PersistentId = "probe-id", Data = new MessageData { ChannelId = "rpd-selftest", Title = title, Body = new Body() } });
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"data\":{\"status\":\"ok\",\"id\":\"fixture-ticket\"}}") };
        }));
        var health = new CompanionPushHealth(http, new CompanionPushHealthTests.AccountService(() => AccountState.Active),
            async (arrival, ct) => { await arrival.WaitAsync(TimeSpan.FromSeconds(3), ct); return true; });
        var listener = new CompanionListener((credentials, ids, receive) => connection = new Connection(ids, receive), Task.Delay, health);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = listener.ListenAsync(files.Profiles, stop.Token).GetAsyncEnumerator();
        await Next(stream); await Next(stream);
        Assert.AreEqual(PushHealthOutcome.Healthy, (await health.CheckAsync(files.Profiles, stop.Token)).Outcome);
        Assert.IsFalse(File.Exists(files.Profiles));
        connection!.Send(Message("actual-chat", "chat"));
        Assert.AreEqual(CompanionUpdateKind.Chat, (await Next(stream)).Kind);
    }

    private sealed class ProbeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct); }
    private static FcmMessage Message(string id, string channel) => new()
    { PersistentId = id, SentAt = DateTime.UtcNow, Data = new MessageData { ChannelId = channel, Title = "Fixture", Message = id, Body = PairingWriterTests.Pairing } };
    private static PairingFiles Configured()
    {
        var files = new PairingFiles();
        File.WriteAllText(files.Config, "{\"fcm_credentials\":{\"gcm\":{\"androidId\":42,\"securityToken\":\"84\"}}}");
        return files;
    }
    private static CompanionListener Service(List<Connection> connections, Func<TimeSpan, CancellationToken, Task>? delay = null)
        => new((credentials, ids, receive) => { var socket = new Connection(ids, receive); connections.Add(socket); return socket; }, delay ?? Task.Delay);

    private sealed class Connection(List<string> ids, Action<FcmMessage> receive) : IFcmConnection
    {
        public event EventHandler? Disconnected;
        public event EventHandler? SocketClosed;
        public event EventHandler<Exception>? ErrorOccurred;
        public IReadOnlyList<string> Seed { get; } = ids.ToArray();
        public bool Disposed { get; private set; }
        public Exception? ConnectError { get; init; }
        public int Subscribers => (Disconnected?.GetInvocationList().Length ?? 0) + (SocketClosed?.GetInvocationList().Length ?? 0) + (ErrorOccurred?.GetInvocationList().Length ?? 0);
        public Task ConnectAsync(CancellationToken ct) => ConnectError is null ? Task.CompletedTask : Task.FromException(ConnectError);
        public void Send(FcmMessage message) => receive(message);
        public void ParseError() => ErrorOccurred?.Invoke(this, new JsonException("fixture parser failure"));
        public void Drop() { Disconnected?.Invoke(this, EventArgs.Empty); SocketClosed?.Invoke(this, EventArgs.Empty); ErrorOccurred?.Invoke(this, new IOException()); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
