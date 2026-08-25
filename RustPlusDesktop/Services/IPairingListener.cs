using RustPlusDesk.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services;

public interface IPairingListener
{
    event EventHandler<PairingPayload>? Paired;

    // Status
    event EventHandler? Listening;
    event EventHandler? RegistrationCompleted;
    event EventHandler? Stopped;
    event EventHandler<string>? Failed;

    // Alarm popups
    event EventHandler<AlarmNotification>? AlarmReceived;
    event EventHandler<TeamChatMessage>? ChatReceived;
    event EventHandler<OfflineDeathNotification>? OfflineDeathReceived;
    event EventHandler<PairingPayload>? ServerInfoReceived;
    bool IsRunning { get; }
    bool IsConfigured { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    // Optional – defaults to the normal start
    Task StartAsyncUsingEdge(CancellationToken ct = default) => StartAsync(ct);
}
