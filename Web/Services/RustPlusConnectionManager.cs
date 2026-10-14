using System.Collections.Concurrent;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RustPlusApi;
using RustPlusWeb.Data;
using RustPlusWeb.Realtime;

namespace RustPlusWeb.Services;

/// <summary>Owns one Rust+ socket per paired server, shared by all authorized browser sessions.</summary>
public sealed class RustPlusConnectionManager(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider protectionProvider,
    IHubContext<ServerHub> hub,
    ILogger<RustPlusConnectionManager> logger) : IHostedService
{
    private readonly IDataProtector _tokenProtector =
        protectionProvider.CreateProtector("RustPlusWeb.PairedServer.PlayerToken.v1");
    private readonly ConcurrentDictionary<Guid, ConnectionEntry> _entries = new();
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public string GetStatus(Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry))
            return "Disconnected";
        if (entry.Status == "Disabled") return "Disabled";
        if (entry.Client?.IsConnected == true) return "Connected";
        return entry.Status == "Connected" ? "Reconnecting" : entry.Status;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopping = Interlocked.Exchange(ref _stopping, null);
        if (stopping is null)
            return;

        stopping.Cancel();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }

        foreach (var entry in _entries.Values)
        {
            await entry.Gate.WaitAsync(CancellationToken.None);
            try
            {
                if (entry.Client is not null)
                {
                    try { await entry.Client.DisconnectAsync(forceClose: true); }
                    catch { /* Shutdown should continue even if a remote socket is already gone. */ }
                    entry.Client.Dispose();
                    entry.Client = null;
                }
            }
            finally { entry.Gate.Release(); }
        }
        stopping.Dispose();
    }

    public async Task EnsureConnectedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.PairedServerCredentials.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (profile is not null)
            await EnsureConnectedAsync(profile, cancellationToken);
    }

    public async Task SetConnectionEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            if (!_entries.TryGetValue(id, out var entry))
                entry = _entries.GetOrAdd(id, _ => new ConnectionEntry());

            await entry.Gate.WaitAsync(cancellationToken);
            try
            {
                if (entry.Client is not null)
                {
                    try { await entry.Client.DisconnectAsync(forceClose: true); }
                    catch { /* The explicit disabled state must still be recorded. */ }
                    entry.Client.Dispose();
                    entry.Client = null;
                }
                entry.Status = "Disabled";
                entry.Failures = 0;
                entry.NextAttemptUtc = DateTime.MaxValue;
            }
            finally { entry.Gate.Release(); }
            await NotifyStatusAsync(id, "Disabled");
            return;
        }

        if (_entries.TryGetValue(id, out var existing))
        {
            await existing.Gate.WaitAsync(cancellationToken);
            try
            {
                existing.Status = "Disconnected";
                existing.NextAttemptUtc = DateTime.MinValue;
                existing.Failures = 0;
            }
            finally { existing.Gate.Release(); }
        }
        await EnsureConnectedAsync(id, cancellationToken);
    }

    public async Task DisconnectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
            return;

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.Client is not null)
            {
                try { await entry.Client.DisconnectAsync(forceClose: true); }
                catch { /* Continue cleanup after a failed disconnect. */ }
                entry.Client.Dispose();
                entry.Client = null;
            }
            entry.Status = "Disconnected";
            entry.Failures = 0;
            entry.NextAttemptUtc = DateTime.MinValue;
        }
        finally { entry.Gate.Release(); }

        await NotifyStatusAsync(id, "Disconnected");
    }

    public async Task<SwitchCommandResult?> SetSwitchAsync(
        Guid serverId, ulong entityId, bool? isActive, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(serverId, out var entry))
            return null;

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.Client is not { IsConnected: true } client)
                return null;

            var response = isActive.HasValue
                ? await client.SetSmartSwitchValueAsync(entityId, isActive.Value, cancellationToken)
                : await client.ToggleSmartSwitchAsync(entityId, cancellationToken);

            return new SwitchCommandResult(response.IsSuccess, response.Data?.IsActive, response.Error?.Message);
        }
        finally { entry.Gate.Release(); }
    }

    public async Task<object?> GetSnapshotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
            return null;

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.Client is not { IsConnected: true } client)
                return null;

            var info = await client.GetInfoAsync(cancellationToken);
            var map = await client.GetMapAsync(cancellationToken);
            var team = await client.GetTeamInfoAsync(cancellationToken);

            return new
            {
                info = info.IsSuccess ? info.Data : null,
                map = map.IsSuccess && map.Data is not null
                    ? new
                    {
                        width = map.Data.Width,
                        height = map.Data.Height,
                        oceanMargin = map.Data.OceanMargin,
                        monuments = map.Data.Monuments,
                        imageBase64 = map.Data.JpgImage is { Length: > 0 }
                            ? Convert.ToBase64String(map.Data.JpgImage)
                            : null
                    }
                    : null,
                team = team.IsSuccess && team.Data is not null
                    ? new
                    {
                        members = team.Data.Members?.Select(member => new
                        {
                            name = member.Name,
                            x = member.X,
                            y = member.Y,
                            isOnline = member.IsOnline,
                            isAlive = member.IsAlive
                        }).ToList()
                    }
                    : null,
                retrievedAtUtc = DateTime.UtcNow
            };
        }
        finally { entry.Gate.Release(); }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profiles = await db.PairedServerCredentials.AsNoTracking().ToListAsync(cancellationToken);
                foreach (var profile in profiles)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (!profile.ConnectionEnabled)
                    {
                        if (!_entries.TryGetValue(profile.Id, out var disabledEntry) || disabledEntry.Status != "Disabled")
                            await SetConnectionEnabledAsync(profile.Id, false, cancellationToken);
                        continue;
                    }
                    await EnsureConnectedAsync(profile, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Rust+ connection supervision failed ({ExceptionType}).",
                    exception.GetType().Name);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    private async Task EnsureConnectedAsync(PairedServerCredential profile, CancellationToken cancellationToken)
    {
        if (!profile.ConnectionEnabled)
        {
            await SetConnectionEnabledAsync(profile.Id, false, cancellationToken);
            return;
        }

        var entry = _entries.GetOrAdd(profile.Id, _ => new ConnectionEntry());
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.Client?.IsConnected == true)
            {
                entry.Status = "Connected";
                return;
            }
            if (DateTime.UtcNow < entry.NextAttemptUtc)
                return;

            entry.Status = "Connecting";
            await NotifyStatusAsync(profile.Id, entry.Status);

            if (entry.Client is null)
            {
                var tokenText = _tokenProtector.Unprotect(profile.ProtectedPlayerToken);
                if (!ulong.TryParse(profile.PlayerId, out var playerId) ||
                    !int.TryParse(tokenText, out var playerToken))
                    throw new InvalidOperationException("Stored Rust+ pairing credentials are invalid.");

                entry.Client = new RustPlus(new RustPlusConnection(
                    profile.Host, profile.Port, playerId, playerToken));
            }

            try
            {
                await entry.Client.ConnectAsync(cancellationToken);
                var info = await entry.Client.GetInfoAsync(cancellationToken);
                if (!info.IsSuccess)
                    throw new InvalidOperationException("The Rust+ server did not return server information.");

                entry.Failures = 0;
                entry.NextAttemptUtc = DateTime.MinValue;
                entry.Status = "Connected";
                await NotifyStatusAsync(profile.Id, entry.Status);
                logger.LogInformation("Rust+ connected for paired server {ServerId}.", profile.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                entry.Client.Dispose();
                entry.Client = null;
                entry.Failures = Math.Min(entry.Failures + 1, 9);
                var delaySeconds = Math.Min(300, Math.Pow(2, entry.Failures));
                entry.NextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
                entry.Status = "Reconnecting";
                logger.LogWarning("Rust+ connect attempt failed for server {ServerId} ({ExceptionType}); retry in {DelaySeconds}s.",
                    profile.Id, exception.GetType().Name, delaySeconds);
                await NotifyStatusAsync(profile.Id, entry.Status);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            entry.Status = "Reconnecting";
            entry.Failures = Math.Min(entry.Failures + 1, 9);
            entry.NextAttemptUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, entry.Failures)));
            logger.LogWarning("Unable to prepare Rust+ connection for server {ServerId} ({ExceptionType}).",
                profile.Id, exception.GetType().Name);
            await NotifyStatusAsync(profile.Id, entry.Status);
        }
        finally { entry.Gate.Release(); }
    }

    private async Task NotifyStatusAsync(Guid id, string status)
    {
        try
        {
            await hub.Clients.All.SendAsync("ServerStatusChanged", new { serverId = id, status });
            await hub.Clients.All.SendAsync("ServersChanged");
        }
        catch (Exception exception)
        {
            logger.LogDebug("Realtime status notification failed ({ExceptionType}).", exception.GetType().Name);
        }
    }

    public sealed record SwitchCommandResult(bool Success, bool? IsActive, string? Error);

    private sealed class ConnectionEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public RustPlus? Client { get; set; }
        public string Status { get; set; } = "Disconnected";
        public int Failures { get; set; }
        public DateTime NextAttemptUtc { get; set; } = DateTime.MinValue;
    }
}
