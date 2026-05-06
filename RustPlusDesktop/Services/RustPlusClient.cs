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
        // Für MVP: Stub-Verbindung (später echte Companion-URL + Auth)
        var uri = new Uri($"ws://{profile.Host}:{profile.Port}/");
        _ws = new ClientWebSocket();

        // Beispiel für Header, wenn nötig:
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

    // Platzhalter für spätere Funktionen:
    public Task ToggleSmartSwitchAsync(long entityId, bool on, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SubscribeRaidAlarmsAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}
