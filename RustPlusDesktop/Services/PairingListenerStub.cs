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
/// Placeholder: simulates an incoming pairing message.
/// Replace later with a real Facepunch/FCM listener.
/// </summary>
public class PairingListenerStub : IPairingListener
{
    public event EventHandler<PairingPayload>? Paired;
    public event EventHandler<TeamChatMessage>? ChatReceived { add { } remove { } }
    private CancellationTokenSource? _cts;
    private readonly Action<string> _log;
    public event EventHandler? Listening;
    public event EventHandler? RegistrationCompleted { add { } remove { } }
    public event EventHandler? Stopped;
    public event EventHandler<string>? Failed { add { } remove { } }

    // Alarm event
    public event EventHandler<AlarmNotification>? AlarmReceived;
    public event EventHandler<OfflineDeathNotification>? OfflineDeathReceived { add { } remove { } }
    public event EventHandler<PairingPayload>? ServerInfoReceived { add { } remove { } }
    private volatile bool _running;
    public bool IsRunning => _running;
    public bool IsConfigured => true;
    public PairingListenerStub(Action<string> log) => _log = log;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _running = true;
        Listening?.Invoke(this, EventArgs.Empty);
        _log("Pairing listener: started (stub). Press CTRL+P in the window to simulate pairing.");
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
        _running = false;
        Stopped?.Invoke(this, EventArgs.Empty);
        _log("Pairing listener: stopped.");
        return Task.CompletedTask;
    }

    // Helper method for simulation
    public void SimulatePairing(PairingPayload p)
        => Paired?.Invoke(this, p);
}
