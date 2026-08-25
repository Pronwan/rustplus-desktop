using RustPlusDesk.Models;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services;

public class RustPlusClient
{
    private ClientWebSocket? _ws;

    public async Task ConnectAsync(ServerProfile profile, CancellationToken ct = default)
    {
        // For MVP: stub connection (real companion URL + auth later)
        var uri = new Uri($"ws://{profile.Host}:{profile.Port}/");
        _ws = new ClientWebSocket();

        // Example for a header if needed:
        // _ws.Options.SetRequestHeader("rusteam", profile.SteamId64);
        // _ws.Options.SetRequestHeader("token", profile.PlayerToken);

        await _ws.ConnectAsync(uri, ct);

        // Stub: einmal "HELLO" senden
        var hello = Encoding.UTF8.GetBytes("HELLO");
        await _ws.SendAsync(hello, WebSocketMessageType.Text, true, ct);
    }

    public async Task DisconnectAsync()
    {
        if (_ws == null) return;
        try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
        catch { /* ignore */ }
        _ws.Dispose();
        _ws = null;
    }

    // Placeholder for later functions:
    public Task ToggleSmartSwitchAsync(long entityId, bool on, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SubscribeRaidAlarmsAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}
