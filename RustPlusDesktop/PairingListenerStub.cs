using System;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RustPlusDesk.Services;

/// <summary>
/// Platzhalter: simuliert eine eingehende Pairing-Nachricht.
/// Später ersetzen durch echten Facepunch/FCM-Listener.
/// </summary>
public class PairingListenerStub : IPairingListener
{
    public event EventHandler<PairingPayload>? Paired;
    public event EventHandler<TeamChatMessage>? ChatReceived;
    private CancellationTokenSource? _cts;
    private readonly Action<string> _log;
    public event EventHandler? Listening;
    public event EventHandler? Stopped;
    public event EventHandler<string>? Failed;
    private (string? server, string? entityName, uint? entityId)? _pendingAlarm;
    private string? _pendingAlarmMsg;
    private DateTime? _pendingAlarmMsgTs;

    public event EventHandler<AlarmNotification>? AlarmReceived;
    private volatile bool _running;
    public bool IsRunning => _running;
    public PairingListenerStub(Action<string> log) => _log = log;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _log("Pairing-Listener: gestartet (Stub). Drücke STRG+P im Fenster, um Pairing zu simulieren.");
        return Task.CompletedTask;
    }
    private void FireAlarm(string? server, string? deviceName, uint? entityId, string message, DateTime ts)
    {
        var srv = server ?? "-";
        var dev = (deviceName ?? "Alarm");
        var alarm = new AlarmNotification(ts, srv, dev, entityId, message);
        AlarmReceived?.Invoke(this, alarm);
        _log($"[{ts:HH:mm:ss}] Alarm | {srv} | {dev}#{(entityId?.ToString() ?? "?")} | \"{message}\"");
    }

    public interface IPairingListener
    {
        event EventHandler<PairingPayload>? Paired;

        // Status
        event EventHandler? Listening;
        event EventHandler? Stopped;
        event EventHandler<string>? Failed;

        // NEU: Alarm-Event
        event EventHandler<AlarmNotification>? AlarmReceived;

        bool IsRunning { get; }

        Task StartAsync(CancellationToken ct = default);
        Task StopAsync();
    }
    public Task StopAsync()
    {
        _cts?.Cancel();
        _cts = null;
        _log("Pairing-Listener: gestoppt.");
        return Task.CompletedTask;
    }

    // Hilfsmethode zum Simulieren
    public void SimulatePairing(PairingPayload p)
        => Paired?.Invoke(this, p);
}
