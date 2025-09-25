using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public class RustPlusClientStub : IRustPlusClient
{
    private readonly Action<string> _log;
    private ClientWebSocket? _ws;

    public RustPlusClientStub(Action<string> log) => _log = log;

    public async Task ConnectAsync(ServerProfile profile, CancellationToken ct = default)
    {
        _ws = new ClientWebSocket();
        var uri = new Uri($"ws://{profile.Host}:{profile.Port}/");

        _log($"WS Connect → {uri}");
        await _ws.ConnectAsync(uri, ct);

        var hello = Encoding.UTF8.GetBytes("HELLO"); // Platzhalter
        await _ws.SendAsync(hello, WebSocketMessageType.Text, true, ct);

        _log("WS verbunden (Stub).");
    }

    public async Task DisconnectAsync()
    {
        if (_ws == null) return;
        try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
        catch { /* ignore */ }
        _ws.Dispose();
        _ws = null;
    }

    public Task ToggleSmartSwitchAsync(long entityId, bool on, CancellationToken ct = default)
    {
        _log($"[Stub] SmartSwitch {(on ? "ON" : "OFF")} für Entity {entityId}");
        return Task.CompletedTask;
    }

    public Task SubscribeRaidAlarmsAsync(CancellationToken ct = default)
    {
        _log("[Stub] Subscribe Raid Alarms");
        return Task.CompletedTask;
    }
}
