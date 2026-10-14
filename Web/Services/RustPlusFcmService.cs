using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusWeb.Data;
using RustPlusWeb.Realtime;

namespace RustPlusWeb.Services;

/// <summary>Owns the self-hosted FCM listener; credentials and replay IDs are protected at rest.</summary>
public sealed class RustPlusFcmService(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider protectionProvider,
    IConfiguration configuration,
    IHubContext<ServerHub> hub,
    RustPlusConnectionManager connections,
    ILogger<RustPlusFcmService> logger) : IHostedService
{
    private readonly IDataProtector _configurationProtector =
        protectionProvider.CreateProtector("RustPlusWeb.FcmConfiguration.v1");
    private readonly IDataProtector _tokenProtector =
        protectionProvider.CreateProtector("RustPlusWeb.PairedServer.PlayerToken.v1");
    private readonly IDataProtector _persistentIdsProtector =
        protectionProvider.CreateProtector("RustPlusWeb.FcmPersistentIds.v1");
    private readonly string _credentialsPath = configuration["FCM_CREDENTIALS_PATH"] ??
        Path.Combine(AppContext.BaseDirectory, "App_Data", "fcm-credentials.protected");
    private readonly string _persistentIdsPath = configuration["FCM_PERSISTENT_IDS_PATH"] ??
        Path.Combine(AppContext.BaseDirectory, "App_Data", "fcm-persistent-ids.protected");
    private readonly object _idsLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private HashSet<string> _persistentIds = new(StringComparer.Ordinal);
    private RustPlusFcm? _listener;
    private string? _ownerUserId;
    private volatile bool _isListening;
    private CancellationTokenSource? _stopping;
    private Task? _reconnectLoop;

    public bool IsConfigured => File.Exists(_credentialsPath);
    public bool IsListening => _isListening;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LoadPersistentIds();

        if (File.Exists(_credentialsPath))
        {
            try
            {
                var protectedJson = await File.ReadAllTextAsync(_credentialsPath, _stopping.Token);
                var saved = JsonSerializer.Deserialize<SavedFcmConfiguration>(
                    _configurationProtector.Unprotect(protectedJson));
                if (saved is null || string.IsNullOrWhiteSpace(saved.OwnerUserId) ||
                    string.IsNullOrWhiteSpace(saved.CredentialsJson))
                    throw new InvalidOperationException("Saved FCM configuration is incomplete.");

                _ownerUserId = saved.OwnerUserId;
                await StartListenerAsync(saved.CredentialsJson, _stopping.Token);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _isListening = false;
                logger.LogWarning("Saved FCM listener could not start ({ExceptionType}); automatic retry is enabled.",
                    exception.GetType().Name);
            }
        }

        _reconnectLoop = RunReconnectLoopAsync(_stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopping = Interlocked.Exchange(ref _stopping, null);
        if (stopping is not null)
        {
            stopping.Cancel();
            if (_reconnectLoop is not null)
            {
                try { await _reconnectLoop; }
                catch (OperationCanceledException) { }
            }
        }

        await _lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            _listener?.Disconnect();
            _listener = null;
            _isListening = false;
        }
        finally
        {
            _lifecycleGate.Release();
            stopping?.Dispose();
        }
    }

    private async Task RunReconnectLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (File.Exists(_credentialsPath) && !_isListening)
            {
                try
                {
                    await _lifecycleGate.WaitAsync(cancellationToken);
                    try
                    {
                        if (!_isListening)
                        {
                            var protectedJson = await File.ReadAllTextAsync(_credentialsPath, cancellationToken);
                            var saved = JsonSerializer.Deserialize<SavedFcmConfiguration>(
                                _configurationProtector.Unprotect(protectedJson));
                            if (saved is not null && !string.IsNullOrWhiteSpace(saved.OwnerUserId) &&
                                !string.IsNullOrWhiteSpace(saved.CredentialsJson))
                            {
                                _ownerUserId = saved.OwnerUserId;
                                await StartListenerCoreAsync(ParseCredentials(saved.CredentialsJson), cancellationToken);
                            }
                        }
                    }
                    finally { _lifecycleGate.Release(); }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _isListening = false;
                    logger.LogWarning("FCM reconnect attempt failed ({ExceptionType}); retrying automatically.",
                        exception.GetType().Name);
                }
            }

            try { await Task.Delay(_isListening ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(15), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    public async Task ConfigureAsync(string credentialsJson, string ownerUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson) || credentialsJson.Length > 128_000)
            throw new ArgumentException("The credentials file is empty or too large.");

        var credentials = ParseCredentials(credentialsJson);
        var normalizedJson = JsonSerializer.Serialize(credentials);
        var saved = JsonSerializer.Serialize(new SavedFcmConfiguration(ownerUserId, normalizedJson));
        Directory.CreateDirectory(Path.GetDirectoryName(_credentialsPath)!);
        await File.WriteAllTextAsync(_credentialsPath, _configurationProtector.Protect(saved), cancellationToken);

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _ownerUserId = ownerUserId;
            await StartListenerCoreAsync(credentials, cancellationToken);
        }
        finally { _lifecycleGate.Release(); }
    }

    private async Task StartListenerAsync(string credentialsJson, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try { await StartListenerCoreAsync(ParseCredentials(credentialsJson), cancellationToken); }
        finally { _lifecycleGate.Release(); }
    }

    private async Task StartListenerCoreAsync(Credentials credentials, CancellationToken cancellationToken)
    {
        _listener?.Disconnect();
        _listener = null;
        _isListening = false;

        var listener = new RustPlusFcm(credentials, _persistentIds);
        listener.Connected += (_, _) =>
        {
            _isListening = true;
            logger.LogInformation("Rust+ FCM listener connected.");
            _ = hub.Clients.All.SendAsync("FcmStatusChanged", new { configured = true, listening = true });
        };
        listener.Disconnected += (_, _) =>
        {
            _isListening = false;
            logger.LogWarning("Rust+ FCM listener disconnected.");
            _ = hub.Clients.All.SendAsync("FcmStatusChanged", new { configured = true, listening = false });
        };
        listener.ErrorOccurred += (_, exception) =>
            logger.LogWarning("Rust+ FCM listener error ({ExceptionType}).", exception.GetType().Name);
        listener.PersistentIdReceived += (_, persistentId) => PersistPersistentId(persistentId);
        listener.OnServerPairing += (_, pairing) => _ = HandlePairingSafelyAsync(pairing);
        listener.OnEntityPairing += (_, pairing) => _ = HandleEntityPairingSafelyAsync(pairing);
        listener.OnAlarmTriggered += (_, alarm) =>
        {
            if (alarm is not null)
                _ = hub.Clients.All.SendAsync("AlarmReceived", new
                {
                    serverId = alarm.ServerId,
                    title = alarm.Title,
                    message = alarm.Message,
                    receivedAtUtc = DateTime.UtcNow
                });
        };

        _listener = listener;
        await listener.ConnectAsync(cancellationToken);
    }

    private async Task HandlePairingSafelyAsync(Notification<RustPlusApi.Fcm.Data.Events.ServerEvent?> pairing)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_ownerUserId) || pairing.Data is not { } server ||
                string.IsNullOrWhiteSpace(server.Ip) || server.Port is < 1 or > 65535 ||
                pairing.PlayerId == 0 || pairing.PlayerToken <= 0)
                return;

            Guid profileId;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profile = await db.PairedServerCredentials.SingleOrDefaultAsync(
                    item => item.RustServerId == pairing.ServerId);

                if (profile is null)
                {
                    profile = new PairedServerCredential
                    {
                        OwnerUserId = _ownerUserId,
                        RustServerId = pairing.ServerId,
                        Name = string.IsNullOrWhiteSpace(server.Name) ? "Rust server" : server.Name,
                        Host = server.Ip,
                        Port = server.Port,
                        PlayerId = pairing.PlayerId.ToString(CultureInfo.InvariantCulture),
                        ProtectedPlayerToken = _tokenProtector.Protect(
                            pairing.PlayerToken.ToString(CultureInfo.InvariantCulture))
                    };
                    db.PairedServerCredentials.Add(profile);
                }
                else
                {
                    profile.OwnerUserId = _ownerUserId;
                    profile.Name = string.IsNullOrWhiteSpace(server.Name) ? profile.Name : server.Name;
                    profile.Host = server.Ip;
                    profile.Port = server.Port;
                    profile.PlayerId = pairing.PlayerId.ToString(CultureInfo.InvariantCulture);
                    profile.ProtectedPlayerToken = _tokenProtector.Protect(
                        pairing.PlayerToken.ToString(CultureInfo.InvariantCulture));
                }

                await db.SaveChangesAsync();
                profileId = profile.Id;
            }

            logger.LogInformation("Received pairing for Rust+ server {ServerId}.", pairing.ServerId);
            await hub.Clients.All.SendAsync("ServersChanged");
            await connections.EnsureConnectedAsync(profileId);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Could not persist a Rust+ pairing event ({ExceptionType}).", exception.GetType().Name);
        }
    }

    private async Task HandleEntityPairingSafelyAsync(
        Notification<RustPlusApi.Fcm.Data.Events.EntityEvent?> pairing)
    {
        try
        {
            var entity = pairing.Data;
            if (entity?.EntityId is not ulong entityId || entity.EntityType is not { } entityType)
                return;

            Guid pairedServerId;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var server = await db.PairedServerCredentials.SingleOrDefaultAsync(
                    item => item.RustServerId == pairing.ServerId);
                if (server is null)
                {
                    logger.LogInformation("Ignoring entity pairing before its server pairing was saved.");
                    return;
                }

                var entityIdText = entityId.ToString(CultureInfo.InvariantCulture);
                var device = await db.PairedDevices.SingleOrDefaultAsync(item =>
                    item.PairedServerCredentialId == server.Id && item.EntityId == entityIdText);
                var typeName = entityType.ToString();
                var name = string.IsNullOrWhiteSpace(entity.EntityName)
                    ? $"{typeName} {entityIdText}"
                    : entity.EntityName;

                if (device is null)
                {
                    device = new PairedDevice
                    {
                        PairedServerCredentialId = server.Id,
                        EntityId = entityIdText,
                        Name = name,
                        Type = typeName
                    };
                    db.PairedDevices.Add(device);
                }
                else
                {
                    device.Name = name;
                    device.Type = typeName;
                }

                await db.SaveChangesAsync();
                pairedServerId = server.Id;
            }

            await hub.Clients.All.SendAsync("DevicesChanged", new { serverId = pairedServerId });
        }
        catch (Exception exception)
        {
            logger.LogWarning("Could not persist a Rust+ entity pairing event ({ExceptionType}).",
                exception.GetType().Name);
        }
    }

    private void LoadPersistentIds()
    {
        if (!File.Exists(_persistentIdsPath))
            return;

        try
        {
            var plain = _persistentIdsProtector.Unprotect(File.ReadAllText(_persistentIdsPath));
            _persistentIds = JsonSerializer.Deserialize<HashSet<string>>(plain) ?? new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception exception)
        {
            logger.LogWarning("FCM de-duplication state could not be read ({ExceptionType}); starting a fresh set.",
                exception.GetType().Name);
            _persistentIds = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void PersistPersistentId(string persistentId)
    {
        if (string.IsNullOrWhiteSpace(persistentId))
            return;

        lock (_idsLock)
        {
            _persistentIds.Add(persistentId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_persistentIdsPath)!);
                var plain = JsonSerializer.Serialize(_persistentIds);
                File.WriteAllText(_persistentIdsPath, _persistentIdsProtector.Protect(plain));
            }
            catch (Exception exception)
            {
                logger.LogWarning("FCM de-duplication state could not be saved ({ExceptionType}).",
                    exception.GetType().Name);
            }
        }
    }

    private static Credentials ParseCredentials(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        try
        {
            var native = JsonSerializer.Deserialize<Credentials>(json, options);
            if (native?.Gcm is { AndroidId: > 0, SecurityToken: > 0 })
                return native;
        }
        catch (JsonException)
        {
            // Try the legacy rustplus.js config shape below.
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("fcm_credentials", out var fcmCredentials) ||
            !fcmCredentials.TryGetProperty("gcm", out var gcm))
            throw new ArgumentException("Credentials must be native RustPlusApi JSON or rustplus.js JSON.");

        var androidId = ReadUlong(gcm, "androidId");
        var securityToken = ReadUlong(gcm, "securityToken");
        if (androidId == 0 || securityToken == 0)
            throw new ArgumentException("The credentials file is missing its GCM Android ID or security token.");

        return new Credentials { Gcm = new Gcm { AndroidId = androidId, SecurityToken = securityToken } };
    }

    private static ulong ReadUlong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt64(out var numeric))
            return numeric;
        if (property.ValueKind == JsonValueKind.String &&
            ulong.TryParse(property.GetString(), CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    private sealed record SavedFcmConfiguration(string OwnerUserId, string CredentialsJson);
}
