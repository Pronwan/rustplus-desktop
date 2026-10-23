using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Infrastructure.Profiles;
using RustPlusDesk.Infrastructure.Storage;

namespace RustPlusDesk.Infrastructure.Accounts;

/// <summary>Serializes push handling, pairing persistence and reconnect behind a credential-free stream.</summary>
public sealed class CompanionListener : ICompanionListener
{
    private readonly Func<Credentials, List<string>, Action<FcmMessage>, IFcmConnection> create;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly CompanionPushHealth? health;
    public CompanionListener() : this((credentials, ids, receive) => new FcmConnection(credentials, ids, receive), Task.Delay) { }
    public CompanionListener(CompanionPushHealth health) : this((credentials, ids, receive) => new FcmConnection(credentials, ids, receive), Task.Delay, health) { }
    internal CompanionListener(Func<Credentials, List<string>, Action<FcmMessage>, IFcmConnection> create, Func<TimeSpan, CancellationToken, Task> delay, CompanionPushHealth? health = null)
    { this.create = create; this.delay = delay; this.health = health; }

    public async IAsyncEnumerable<CompanionUpdate> ListenAsync(string profilesPath, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var updates = Channel.CreateUnbounded<CompanionUpdate>(new() { SingleReader = true });
        Task worker = RunAsync(Path.GetFullPath(profilesPath), updates.Writer, lifetime.Token);
        try
        {
            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return update;
        }
        finally { await lifetime.CancelAsync().ConfigureAwait(false); await worker.ConfigureAwait(false); }
    }

    private async Task RunAsync(string path, ChannelWriter<CompanionUpdate> output, CancellationToken ct)
    {
        try
        {
            string directory = Path.GetDirectoryName(path)!;
            int attempt = 0;
            var recentPairing = new RecentPairing();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var credentials = await ReadCredentialsAsync(Path.Combine(directory, "rustplusjs-config.json"), ct).ConfigureAwait(false);
                string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.Gcm.AndroidId.ToString(CultureInfo.InvariantCulture))));
                string idsPath = Path.Combine(directory, $"fcm-persistent-ids.{key}.json");
                byte[]? originalIds = await AtomicPrivateFile.ReadAsync(idsPath, ct).ConfigureAwait(false);
                byte[]? seed = originalIds ?? await AtomicPrivateFile.ReadAsync(Path.Combine(directory, "fcm-persistent-ids.json"), ct).ConfigureAwait(false);
                var processed = seed is null ? [] : (JsonSerializer.Deserialize<List<string>>(seed) ?? throw new JsonException())
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().TakeLast(1000).ToList();
                output.TryWrite(new(attempt == 0 ? CompanionUpdateKind.Connecting : CompanionUpdateKind.Reconnecting,
                    "Rust+ pairing", attempt == 0 ? "Connecting to Rust+ notifications…" : "Reconnecting to Rust+ notifications…"));
                try
                {
                    await RunConnectionAsync(path, credentials, processed, output, recentPairing, () => attempt = 0, async id =>
                    {
                        if (string.IsNullOrEmpty(id)) return;
                        processed.Remove(id);
                        processed.Add(id);
                        if (processed.Count > 1000) processed.RemoveRange(0, processed.Count - 1000);
                        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(processed);
                        await AtomicPrivateFile.WriteAsync(idsPath, bytes, originalIds, false, ct).ConfigureAwait(false);
                        originalIds = bytes;
                    }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or ObjectDisposedException)
                {
                    attempt++;
                    var backoff = TimeSpan.FromSeconds(Math.Min(60, 3 * Math.Pow(2, Math.Min(attempt - 1, 5))));
                    output.TryWrite(new(CompanionUpdateKind.Reconnecting, "Rust+ pairing", $"Connection lost. Retrying in {backoff.TotalSeconds:0} seconds…"));
                    await delay(backoff, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { output.TryComplete(); }
        catch (CompanionListenerException ex) { output.TryComplete(ex); }
        catch (Exception) { output.TryComplete(new CompanionListenerException(CompanionListenerFailure.StorageUnavailable)); }
    }

    private async Task RunConnectionAsync(string path, Credentials credentials, List<string> processed, ChannelWriter<CompanionUpdate> output,
        RecentPairing recentPairing, Action connected, Func<string, Task> remember, CancellationToken ct)
    {
        var messages = Channel.CreateUnbounded<FcmMessage>(new() { SingleReader = true });
        await using var connection = create(credentials, new List<string>(processed), message => messages.Writer.TryWrite(message));
        void Disconnected(object? sender, EventArgs args) => messages.Writer.TryComplete(new IOException());
        void Error(object? sender, Exception error)
        {
            if (error is IOException or SocketException or TimeoutException or ObjectDisposedException) Disconnected(sender, EventArgs.Empty);
            else output.TryWrite(new(CompanionUpdateKind.Notice, "Rust+ notifications", "A notification could not be read."));
        }
        connection.Disconnected += Disconnected;
        connection.SocketClosed += Disconnected;
        connection.ErrorOccurred += Error;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            try { await connection.ConnectAsync(connectTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(); }
            connected();
            output.TryWrite(new(CompanionUpdateKind.Listening, "Rust+ pairing", "Listening. Pair a server or device from Rust to add it here."));
            await foreach (var message in messages.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(message.PersistentId) && processed.Contains(message.PersistentId)) continue;
                if (message.Data is null) continue;
                if (health?.TryConsume(path, message.Data.Title) == true)
                {
                    try { await remember(message.PersistentId).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception) { throw new CompanionListenerException(CompanionListenerFailure.StorageUnavailable); }
                    continue;
                }
                var update = CompanionPushParser.Parse(message, DateTimeOffset.UtcNow);
                try
                {
                    if (update.Kind == CompanionUpdateKind.Pairing)
                    {
                        if (message.Data.Body is null) throw new ArgumentException();
                        var body = message.Data.Body;
                        string signature = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                        { body.Ip, body.Port, body.PlayerId, body.PlayerToken, body.EntityId })));
                        if (signature == recentPairing.Signature && DateTimeOffset.UtcNow - recentPairing.At < TimeSpan.FromSeconds(20))
                        {
                            await remember(message.PersistentId).ConfigureAwait(false);
                            continue;
                        }
                        LegacyPairingWriter.Result saved;
                        try { saved = await LegacyPairingWriter.SaveAsync(path, message.Data.Body, ct).ConfigureAwait(false); }
                        catch (RustPlusDesk.Core.Servers.ProfileChangedException)
                        {
                            output.TryWrite(new(CompanionUpdateKind.Notice, "Pairing not saved", "The server match is ambiguous or profiles changed. Refresh profiles and pair again."));
                            continue;
                        }
                        recentPairing.Signature = signature;
                        recentPairing.At = DateTimeOffset.UtcNow;
                        update = update with
                        {
                            ProfileKey = saved.Server.ProfileKey,
                            ReplacesProfileKey = saved.PreviousProfileKey,
                            ProfilesChanged = true,
                            Title = update.EntityId.HasValue ? message.Data.Body.EntityName ?? "Smart Device" : saved.Server.Name,
                            Message = update.EntityId.HasValue ? "Device paired." : "Server paired."
                        };
                    }
                    else if (message.Data.Body is { } metadata)
                    {
                        try { update = update with { ProfilesChanged = await LegacyPairingWriter.UpdateMetadataAsync(path, metadata, ct).ConfigureAwait(false) }; }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception) { output.TryWrite(new(CompanionUpdateKind.Notice, "Server details not updated", "The notification was received, but saved server details could not be updated. Check your local profiles file.")); }
                    }
                    output.TryWrite(update);
                    await remember(message.PersistentId).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
                {
                    output.TryWrite(new(CompanionUpdateKind.Notice, "Pairing not saved", "The pairing data is invalid. Pair the server or device again."));
                }
                catch (Exception) { throw new CompanionListenerException(CompanionListenerFailure.StorageUnavailable); }
            }
        }
        finally
        {
            connection.Disconnected -= Disconnected;
            connection.SocketClosed -= Disconnected;
            connection.ErrorOccurred -= Error;
            messages.Writer.TryComplete();
        }
    }

    private static async Task<Credentials> ReadCredentialsAsync(string path, CancellationToken ct)
    {
        var config = await CompanionConfigFile.ReadAsync(path, ct).ConfigureAwait(false);
        if (config.Original is null) throw new CompanionListenerException(CompanionListenerFailure.MissingCredentials);
        try
        {
            var gcm = config.Values["fcm_credentials"]?["gcm"];
            ulong Read(string field) => ulong.TryParse(gcm?[field]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
            ulong android = Read("androidId"), token = Read("securityToken");
            if (android == 0 || token == 0) throw new CompanionListenerException(CompanionListenerFailure.InvalidCredentials);
            return new Credentials { Gcm = new Gcm { AndroidId = android, SecurityToken = token } };
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        { throw new CompanionListenerException(CompanionListenerFailure.InvalidCredentials); }
    }

    private sealed class RecentPairing
    {
        public string? Signature { get; set; }
        public DateTimeOffset At { get; set; }
    }
}
