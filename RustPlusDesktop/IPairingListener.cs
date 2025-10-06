using RustPlusDesk.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface IPairingListener
{
    event EventHandler<PairingPayload>? Paired;

    // Status
    event EventHandler? Listening;
    event EventHandler? Stopped;
    event EventHandler<string>? Failed;

    // NEU: Alarm-Popups
    event EventHandler<AlarmNotification>? AlarmReceived;
    event EventHandler<TeamChatMessage>? ChatReceived;
    bool IsRunning { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    // NEU: optional – Standard fällt auf normalen Start zurück
    Task StartAsyncUsingEdge(CancellationToken ct = default) => StartAsync(ct);
}
