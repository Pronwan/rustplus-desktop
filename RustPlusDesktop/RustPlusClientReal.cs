// Services/RustPlusClientReal.cs
using RustPlusApi;                 // NuGet: HandyS11.RustPlusApi
using RustPlusApi.Data.Events;
using RustPlusDesk.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq;
using System.Text; // <— hinzufügen
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TeamMsgArg = RustPlusApi.Data.Events.TeamMessageEventArg;
namespace RustPlusDesk.Services;



public sealed class RustPlusClientReal : IRustPlusClient, IDisposable
{
    private RustPlus? _api;
    private readonly Action<string> _log;
    private string? _host;
    private int _port;
    private ulong _steamId;
    private int _playerToken;
    private bool _useProxyCurrent;
    private const int RequestTimeoutMs = 2500;
    public RustPlusClientReal(Action<string> log) => _log = log;
    public event Action<uint, bool, string?>? DeviceStateEvent;

    // ---------- TEAM-CHAT ----------
    
    private bool _chatHooked;
    public event EventHandler<TeamChatMessage>? TeamChatReceived;
    // Overload ohne Token (falls irgendwo so aufgerufen wird)
    public Task ConnectAsync(ServerProfile profile) =>
    ConnectAsync(profile, CancellationToken.None);

    private static T ReadProp<T>(object src, params string[] names)
    {
        var t = src.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n);
            if (p != null && p.PropertyType != typeof(void))
            {
                var v = p.GetValue(src);
                if (v is T tv) return tv;

                // z.B. Items ist IEnumerable → Count nehmen
                if (typeof(T) == typeof(int) && v is System.Collections.IEnumerable en)
                {
                    int c = 0; foreach (var _ in en) c++;
                    return (T)(object)c;
                }
            }
        }
        return default!;
    }

    private static uint GetEntityId(object e) => ReadProp<uint>(e, "EntityId", "Id");
    private static bool GetIsActive(object e) => ReadProp<bool>(e, "IsActive", "Value", "On");
    private static int GetCapacity(object e) => ReadProp<int>(e, "Capacity");
    private static int GetItemsCount(object e) => ReadProp<int>(e, "ItemsCount", "Items", "ItemCount");

    

    private bool _eventsHooked;

    // Rust+ feuert dieses Event, sobald der Chat „geprimed“ wurde.
    // Wir mappen es auf unser eigenes DTO und reichen es weiter.
    private void Api_OnTeamChatReceived(object? sender, TeamMessageEventArg e)
    {
        try
        {
            // Deine Lib: Werte hängen direkt am EventArgs (Username/Name, Message, Time …).
            // Andere Libs: könnten anders heißen. Wir lesen defensiv via Reflection.
            string author = TryGetStringProp(e, "Username", "Name", "User") ?? "Unbekannt";
            string text = TryGetStringProp(e, "Message", "Body", "Text") ?? string.Empty;

            long? unix = TryGetLongishProp(e, "Time", "Timestamp");

            var tsUtc = unix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(unix.Value).UtcDateTime
                : DateTime.UtcNow;

            TeamChatReceived?.Invoke(this, new TeamChatMessage(tsUtc, author, text));
        }
        catch
        {
            // Chat darf nichts reißen
        }
    }


    // --- Helper: in derselben Klasse einfügen -----------------------------------
    private static string? TryGetStringProp(object src, params string[] names)
    {
        var t = src.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n);
            if (p == null) continue;
            if (p.GetValue(src) is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    private static long? TryGetLongishProp(object src, params string[] names)
    {
        var t = src.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n);
            if (p == null) continue;
            var v = p.GetValue(src);
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is double d) return (long)d;
            if (v is DateTime dt) return new DateTimeOffset(dt).ToUnixTimeSeconds();
            if (v is string s && long.TryParse(s, out var lp)) return lp;
        }
        return null;
    }

    private void HookEventsIfNeeded()
    {
        if (_eventsHooked || _api is null) return;

        _api.OnSmartSwitchTriggered += (_, sw) =>
        {
            var id = GetEntityId(sw);
            var on = GetIsActive(sw);
            DeviceStateEvent?.Invoke(id, on, "SmartSwitch");
            _log($"[Gerät] {id} → {(on ? "AN" : "AUS")}");
            
        };

        _api.OnStorageMonitorTriggered += (_, st) =>
            _log($"[Storage] {st.Id} cap={st.Capacity} filled={st.Items}");

        _eventsHooked = true;
    }

 


    // Einmalige Anfrage senden, damit die Lib den Chat-Stream aktiviert
    public async Task PrimeTeamChatAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        // Event einmalig verdrahten
        try
        {
            _api.OnTeamChatReceived -= Api_OnTeamChatReceived;
            _api.OnTeamChatReceived += Api_OnTeamChatReceived;
        }
        catch { /* tolerant */ }

        // Einmaliger „Prime“-Call, damit Events danach geliefert werden
        try { _ = await GetTeamChatHistoryAsync(ct: ct).ConfigureAwait(false); } catch { /* egal */ }
    }

    // Kleiner Helfer wie an anderer Stelle bereits genutzt:
    private static T? Read<T>(object? src, params string[] names)
    {
        if (src is null) return default;
        if (src is T ok) return ok;

        var t = src.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p is null) continue;
            var v = p.GetValue(src);

            if (v is T ok2) return ok2;

            try
            {
                if (v != null)
                {
                    var target = typeof(T);
                    if (target.IsEnum) return (T)Enum.Parse(target, v.ToString()!);
                    return (T)Convert.ChangeType(v, target);
                }
            }
            catch { /* ignore */ }
        }
        return default;
    }

   

    private void OnTeamChatReceivedGeneric<T>(object? _, T evArg)
    {
        // evArg enthält je nach Lib z.B. { Author/Username/Name, Message/Text, Timestamp/Time, ... }
        object msg = evArg!;

        // Zeitstempel
        DateTime ts = DateTime.Now;
        var dt = Read<DateTime?>(msg, "Timestamp", "Time", "Date", "CreatedAt");
        if (dt.HasValue) ts = dt.Value;
        else
        {
            var unix = Read<long?>(msg, "Timestamp", "Time", "Date", "CreatedAt", "Epoch");
            if (unix.HasValue)
            {
                var v = unix.Value;
                var dto = v > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(v)
                    : DateTimeOffset.FromUnixTimeSeconds(v);
                ts = dto.LocalDateTime;
            }
        }

        string author = Read<string>(msg, "Author", "Username", "Name", "PlayerName") ?? "Team";
        string text = Read<string>(msg, "Message", "Text", "Body", "Content") ?? "";

        if (!string.IsNullOrWhiteSpace(text))
            TeamChatReceived?.Invoke(this, new TeamChatMessage(ts, author, text));
    }

    // ==== Helper: Chat-Mapping für beliebige Lib-Versionen ====

    private static IEnumerable<TeamChatMessage> TryMapChatEnumerable(object? listObj)
    {
        if (listObj is not System.Collections.IEnumerable en)
            yield break;

        foreach (var it in en)
        {
            if (it is null) continue;
            var t = it.GetType();

            string? text =
                t.GetProperty("Message")?.GetValue(it) as string ??
                t.GetProperty("Body")?.GetValue(it) as string ??
                t.GetProperty("Text")?.GetValue(it) as string;

            if (string.IsNullOrWhiteSpace(text))
                yield break; // das ist keine Chatliste

            string author =
                t.GetProperty("Name")?.GetValue(it) as string ??
                t.GetProperty("Username")?.GetValue(it) as string ??
                t.GetProperty("User")?.GetValue(it) as string ??
                "Unbekannt";

            long? unix =
                t.GetProperty("Time")?.GetValue(it) as long? ??
                t.GetProperty("Timestamp")?.GetValue(it) as long?;

            var tsLocal = unix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(unix.Value).LocalDateTime
                : DateTime.Now;

            yield return new TeamChatMessage(tsLocal, author, text);
        }
    }

    private List<TeamChatMessage> ExtractChatCandidates(object? root, int depth = 0)
    {
        var acc = new List<TeamChatMessage>();
        if (root is null || depth > 4) return acc;

        // 1) Ist das Ding selbst schon eine Chatliste?
        var mapped = TryMapChatEnumerable(root).ToList();
        if (mapped.Count > 0) { acc.AddRange(mapped); return acc; }

        // 2) Sonst in Properties weiter kriechen
        var tp = root.GetType();
        foreach (var p in tp.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            object? v = null;
            try { v = p.GetValue(root); } catch { }
            if (v is null) continue;

            // kleine Abkürzung: typische Namen bevorzugen
            if (p.Name.Contains("Chat", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("Team", StringComparison.OrdinalIgnoreCase))
            {
                var mm = TryMapChatEnumerable(v).ToList();
                if (mm.Count > 0) { acc.AddRange(mm); continue; }
            }

            acc.AddRange(ExtractChatCandidates(v, depth + 1));
        }
        return acc;
    }

    public async Task<List<TeamChatMessage>> GetTeamChatHistoryAsync(
      DateTime? sinceUtc = null, int? limit = null, CancellationToken ct = default)
    {
        var result = new List<TeamChatMessage>();
        if (_api is null) return result;

        try
        {
            // 1) Versuche dedizierte Methoden zuerst
            object? resObj = null;
            var apiType = _api.GetType();

            var mHist = apiType.GetMethod("GetTeamChatHistoryAsync")
                       ?? apiType.GetMethod("GetTeamChatAsync");              // anderes Lib-Label
            if (mHist != null)
            {
                var ps = mHist.GetParameters();
                object?[] args = Array.Empty<object?>();

                // häufige Signaturen abdecken
                if (ps.Length == 2 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(CancellationToken))
                    args = new object?[] { limit ?? 100, ct };
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                    args = new object?[] { limit ?? 100 };
                else if (ps.Length == 1 && ps[0].ParameterType.Name.Contains("CancellationToken"))
                    args = new object?[] { ct };

                resObj = await UnwrapTaskAsync(mHist.Invoke(_api, args));
            }
            else
            {
                // 2) Fallback: TeamInfo holen (viele Libs liefern Chat dort mit)
                var mInfo = apiType.GetMethod("GetTeamInfoAsync");
                if (mInfo != null)
                {
                    var ps = mInfo.GetParameters();
                    object?[] args = (ps.Length == 1 && ps[0].ParameterType == typeof(CancellationToken))
                                     ? new object?[] { ct } : Array.Empty<object?>();
                    resObj = await UnwrapTaskAsync(mInfo.Invoke(_api, args));
                }
            }

            if (resObj is null) { _log("[chat-history] mapped=0 afterFilter=0 since=" + (sinceUtc?.ToString("u") ?? "null")); return result; }

            // 3) Response<T> → .Data entpacken
            var dataProp = resObj.GetType().GetProperty("Data");
            var root = dataProp?.GetValue(resObj) ?? resObj;

            // 4) Direkte Felder versuchen, sonst rekursiv scannen
            var chatRoot = TryGet(root, "TeamChat")
                        ?? TryGet(root, "Chat")
                        ?? TryGet(root, "Messages")
                        ?? root;

            var mapped = ExtractChatCandidates(chatRoot); // deine vorhandene Rekursion
            var filtered = sinceUtc.HasValue
                ? mapped.Where(m => m.Timestamp > sinceUtc.Value).ToList()
                : mapped;

            _log($"[chat-history] mapped={mapped.Count} afterFilter={filtered.Count} since={(sinceUtc?.ToString("u") ?? "null")}");
            return filtered.OrderBy(m => m.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _log("[chat-history:error] " + ex.Message);
            return result;
        }
    }

    private static object? TryGet(object? o, string name)
    => o?.GetType().GetProperty(name)?.GetValue(o);

    // Task/ValueTask dynamisch entpacken
    private static async Task<object?> UnwrapTaskAsync(object? taskOrValue)
    {
        if (taskOrValue is null) return null;
        switch (taskOrValue)
        {
            case Task t when t.GetType().IsGenericType:
                await t.ConfigureAwait(false);
                return t.GetType().GetProperty("Result")?.GetValue(t);
            case Task t:
                await t.ConfigureAwait(false);
                return null;
            default:
                return taskOrValue;
        }
    }


    private static PropertyInfo? FindPropCI(Type t, params string[] candidates)
    {
        foreach (var p in t.GetProperties())
        {
            var pn = p.Name.Replace("_", "");
            foreach (var c in candidates)
            {
                var cn = c.Replace("_", "");
                if (string.Equals(pn, cn, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
        }
        return null;
    }

    private static int? ToInt(object? v)
    {
        if (v == null) return null;
        return v switch
        {
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => unchecked((int)ui),
            long l => (l > int.MaxValue ? int.MaxValue : (int)l),
            ulong ul => (ul > int.MaxValue ? int.MaxValue : (int)ul),
            float f => (int)f,
            double d => (int)d,
            decimal m => (int)m,
            string s when int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    private static int? ReadIntCI(object? src, params string[] names)
    {
        if (src == null) return null;
        var t = src.GetType();

        // 1) direkt
        var p = FindPropCI(t, names);
        if (p != null) return ToInt(p.GetValue(src));

        // 2) häufige Container
        var sub = t.GetProperty("Server")?.GetValue(src)
               ?? t.GetProperty("ServerInfo")?.GetValue(src);
        if (sub != null)
        {
            var sp = FindPropCI(sub.GetType(), names);
            if (sp != null) return ToInt(sp.GetValue(sub));
        }
        return null;
    }

    private static string? ReadStringCI(object? src, params string[] names)
    {
        if (src == null) return null;
        var t = src.GetType();
        var p = FindPropCI(t, names);
        if (p != null && p.GetValue(src) is string s && !string.IsNullOrWhiteSpace(s)) return s;

        var sub = t.GetProperty("Server")?.GetValue(src)
               ?? t.GetProperty("ServerInfo")?.GetValue(src);
        if (sub != null)
        {
            var sp = FindPropCI(sub.GetType(), names);
            if (sp != null && sp.GetValue(sub) is string s2 && !string.IsNullOrWhiteSpace(s2)) return s2;
        }
        return null;
    }
    public record MonumentMarker(string Name, double X, double Y);

    public async Task<(int WorldSize, int MapWidth, int MapHeight, List<MonumentMarker> Monuments)>




    GetWorldAndMonumentsAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        static int ReadInt(object? obj, params string[] names)
        {
            if (obj == null) return 0;
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n);
                if (p?.GetValue(obj) is int i) return i;
                if (int.TryParse(p?.GetValue(obj)?.ToString(), out var ii)) return ii;
            }
            return 0;
        }
        static double ReadDouble(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n);
                var v = p?.GetValue(obj);
                if (v is double d) return d;
                if (double.TryParse(v?.ToString(), out var dd)) return dd;
            }
            return 0.0;
        }
        static string? ReadString(object obj, params string[] names)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n);
                var v = p?.GetValue(obj);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        int worldSize = 0, mapW = 0, mapH = 0;
        var monuments = new List<MonumentMarker>();

        // WorldSize aus GetInfoAsync
        try
        {
            var t = _api.GetType();
            var m = t.GetMethod("GetInfoAsync", new[] { typeof(CancellationToken) })
                 ?? t.GetMethod("GetInfoAsync", Type.EmptyTypes);
            if (m != null)
            {
                var call = m.GetParameters().Length == 1 ? m.Invoke(_api, new object[] { ct })
                                                         : m.Invoke(_api, Array.Empty<object>());
                if (call is Task task)
                {
                    await task.ConfigureAwait(false);
                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                    var data = result?.GetType().GetProperty("Data")?.GetValue(result) ?? result;
                    worldSize = ReadInt(data, "WorldSize", "MapSize");
                }
            }
        }
        catch { }

        // Monuments + MapWidth/Height aus GetMapAsync
        try
        {
            var t = _api.GetType();
            var m = t.GetMethod("GetMapAsync", new[] { typeof(CancellationToken) })
                 ?? t.GetMethod("GetMapAsync", Type.EmptyTypes);
            if (m != null)
            {
                var call = m.GetParameters().Length == 1 ? m.Invoke(_api, new object[] { ct })
                                                         : m.Invoke(_api, Array.Empty<object>());
                if (call is Task task)
                {
                    await task.ConfigureAwait(false);
                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                    var mapObj = result?.GetType().GetProperty("Data")?.GetValue(result) ?? result;

                    mapW = ReadInt(mapObj, "Width");
                    mapH = ReadInt(mapObj, "Height");

                    var listObj = mapObj?.GetType().GetProperty("Monuments")?.GetValue(mapObj);
                    if (listObj is System.Collections.IEnumerable items)
                    {
                        foreach (var it in items)
                        {
                            var tp = it!.GetType();
                            var pos = tp.GetProperty("Position")?.GetValue(it);

                            double x = pos != null ? ReadDouble(pos, "X") : ReadDouble(it, "X");
                            double y = pos != null ? ReadDouble(pos, "Y") : ReadDouble(it, "Y");
                            string name = ReadString(it, "Name", "Alias", "Token") ?? "Monument";

                            monuments.Add(new MonumentMarker(name, x, y));
                        }
                    }
                }
            }
        }
        catch { }

        return (worldSize, mapW, mapH, monuments);
    }

    public sealed class MapWithMonuments
{
    public required BitmapSource Bitmap { get; init; }
    public required int PixelWidth  { get; init; }
    public required int PixelHeight { get; init; }
    public required int WorldSize   { get; init; } // falls vorhanden, sonst 0
    public required List<(double X, double Y, string Name)> Monuments { get; init; }
}



    public async Task<MapWithMonuments?> GetMapWithMonumentsAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        static byte[]? ReadBytesFlexible(object? obj, params string[] names)
        {
            if (obj is null) return null;
            if (obj is byte[] b1) return b1;
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n);
                var v = p?.GetValue(obj);
                if (v is byte[] bb) return bb;
                if (v is string s) { try { return Convert.FromBase64String(s); } catch { } }
            }
            return null;
        }

        static BitmapSource? ToBitmap(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        try
        {
            var t = _api.GetType();

            // -------- 1) Map holen (Bild + Maße + evtl. Monuments + evtl. WorldSize) --------
            var mMap = t.GetMethod("GetMapAsync", new[] { typeof(CancellationToken) })
                     ?? t.GetMethod("GetMapAsync", Type.EmptyTypes);
            if (mMap is null) return null;

            object? call = mMap.GetParameters().Length == 1
                ? mMap.Invoke(_api, new object[] { ct })
                : mMap.Invoke(_api, Array.Empty<object>());

            if (call is not Task task) return null;
            await task.ConfigureAwait(false);

            var result = task.GetType().GetProperty("Result")?.GetValue(task);
            var data = result?.GetType().GetProperty("Data")?.GetValue(result) ?? result;
            if (data is null) return null;

            // Bildbytes
            var bytes = ReadBytesFlexible(data, "PngImage", "JpgImage", "Image", "Bytes", "Data");
            var bmp = bytes is null ? null : ToBitmap(bytes);
            if (bmp is null) return null;

            // Map-Pixelmaße
            int mapW = Convert.ToInt32(data.GetType().GetProperty("Width")?.GetValue(data) ?? bmp.PixelWidth);
            int mapH = Convert.ToInt32(data.GetType().GetProperty("Height")?.GetValue(data) ?? bmp.PixelHeight);

            // WorldSize (falls vorhanden – viele Builds haben hier 0)
            int world = Convert.ToInt32(
                data.GetType().GetProperty("WorldSize")?.GetValue(data)
             ?? data.GetType().GetProperty("MapSize")?.GetValue(data)
             ?? 0);

            // Monuments aus dieser Antwort
            var monsList = new List<(double X, double Y, string Name)>();
            if (data.GetType().GetProperty("Monuments")?.GetValue(data) is System.Collections.IEnumerable items)
            {
                foreach (var mo in items)
                {
                    var mt = mo!.GetType();
                    var pos = mt.GetProperty("Position")?.GetValue(mo);

                    double x = pos != null
                        ? Convert.ToDouble(pos.GetType().GetProperty("X")?.GetValue(pos) ?? 0)
                        : Convert.ToDouble(mt.GetProperty("X")?.GetValue(mo) ?? 0);

                    double y = pos != null
                        ? Convert.ToDouble(pos.GetType().GetProperty("Y")?.GetValue(pos) ?? 0)
                        : Convert.ToDouble(mt.GetProperty("Y")?.GetValue(mo) ?? 0);

                    string name =
                        (string?)mt.GetProperty("Name")?.GetValue(mo) ??
                        (string?)mt.GetProperty("Alias")?.GetValue(mo) ??
                        (string?)mt.GetProperty("Token")?.GetValue(mo) ?? "";

                    monsList.Add((x, y, name));
                }
            }

            // -------- 2) Fallback: WorldSize per GetInfoAsync nachladen, falls 0 --------
            if (world <= 0)
            {
                try
                {
                    var mInfo = t.GetMethod("GetInfoAsync", new[] { typeof(CancellationToken) })
                             ?? t.GetMethod("GetInfoAsync", Type.EmptyTypes);
                    if (mInfo != null)
                    {
                        object? callInfo = mInfo.GetParameters().Length == 1
                            ? mInfo.Invoke(_api, new object[] { ct })
                            : mInfo.Invoke(_api, Array.Empty<object>());

                        if (callInfo is Task tInfo)
                        {
                            await tInfo.ConfigureAwait(false);
                            var res = tInfo.GetType().GetProperty("Result")?.GetValue(tInfo);
                            var info = res?.GetType().GetProperty("Data")?.GetValue(res) ?? res;
                            world = Convert.ToInt32(
                                info?.GetType().GetProperty("WorldSize")?.GetValue(info)
                             ?? info?.GetType().GetProperty("MapSize")?.GetValue(info)
                             ?? 0);
                        }
                    }
                }
                catch { /* tolerant */ }
            }

            // -------- 3) Letzter Fallback: robust aus Monuments kalibrieren --------
            if (world <= 0 && monsList.Count > 0)
            {
                var xs = monsList.Select(m => m.X).OrderBy(v => v).ToList();
                var ys = monsList.Select(m => m.Y).OrderBy(v => v).ToList();

                static double Quantile(List<double> s, double p)
                {
                    if (s.Count == 0) return 0;
                    if (p <= 0) return s.First();
                    if (p >= 1) return s.Last();
                    double pos = p * (s.Count - 1);
                    int i = (int)Math.Floor(pos);
                    double frac = pos - i;
                    return i + 1 < s.Count ? s[i] * (1 - frac) + s[i + 1] * frac : s[i];
                }

                // 99%-Box (robust gegen Ausreißer)
                var wx1 = Quantile(xs, 0.99);
                var wy1 = Quantile(ys, 0.99);
                world = (int)Math.Max(wx1, wy1);
            }

            return new MapWithMonuments
            {
                Bitmap = bmp,
                PixelWidth = mapW,
                PixelHeight = mapH,
                WorldSize = world,
                Monuments = monsList
            };
        }
        catch (Exception ex)
        {
            _log("GetMapWithMonumentsAsync Fehler: " + ex.Message);
            return null;
        }
    }
    public async Task<BitmapSource?> GetMapBitmapAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        void Log(string s) => _log?.Invoke("[Map] " + s);

        static bool LooksLikeImage(byte[] b)
        {
            // PNG: 89 50 4E 47 ; JPG: FF D8
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xD8) return true;
            return false;
        }

        static object? GetProp(object? obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var p = t.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            return p?.GetValue(obj);
        }

        static byte[]? ReadBytesFlexible(object? obj, params string[] names)
        {
            if (obj is null) return null;
            if (obj is byte[] b1) return b1;

            // erst direkte Props
            foreach (var n in names)
            {
                var v = GetProp(obj, n);
                if (v is byte[] bb) return bb;
                if (v is string s)
                {
                    try { return Convert.FromBase64String(s); } catch { /* ignore */ }
                }
            }

            // ggf. "Data" tief verschachtelt (result.Data, response.Map.Data etc.)
            var data = GetProp(obj, "Data");
            if (data is byte[] bb2) return bb2;
            if (data is string s2)
            {
                try { return Convert.FromBase64String(s2); } catch { }
            }

            return null;
        }

        static BitmapSource? ToBitmap(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // -------- Pfad 1: Methode GetMapAsync / GetMap (beliebige Signaturen) --------
        try
        {
            var t = _api.GetType();
            var m = t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                     .FirstOrDefault(mi => string.Equals(mi.Name, "GetMapAsync", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(mi.Name, "GetMap", StringComparison.OrdinalIgnoreCase));

            if (m != null)
            {
                // Parameter mit Default auffüllen (CancellationToken, Request, nichts, …)
                var pars = m.GetParameters();
                object?[] args = new object?[pars.Length];
                for (int i = 0; i < pars.Length; i++)
                {
                    var p = pars[i];
                    if (p.ParameterType == typeof(CancellationToken)) args[i] = ct;
                    else if (p.HasDefaultValue) args[i] = p.DefaultValue;
                    else args[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                }

                var call = m.Invoke(_api, args);
                object? resultObj = call;

                if (call is Task task)
                {
                    await task.ConfigureAwait(false);
                    resultObj = task.GetType().GetProperty("Result")?.GetValue(task);
                }

                // häufig: result.Data oder direkt result
                var data = GetProp(resultObj!, "Data") ?? resultObj;
                // manchmal steckt’s in Response/Map
                var response = GetProp(resultObj!, "Response") ?? resultObj;
                var map = GetProp(response, "Map") ?? data;

                var bytes = ReadBytesFlexible(map, "PngImage", "JpgImage", "Image", "Bytes", "Data");
                if (bytes != null && LooksLikeImage(bytes))
                {
                    Log($"Pfad1 OK: {bytes.Length} Bytes ({(bytes[0] == 0x89 ? "PNG" : "JPG/Other")}).");
                    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "rust_map_debug.jpg"), bytes);
                    Log("Map gespeichert unter: " + Path.Combine(Path.GetTempPath(), "rust_map_debug.jpg"));
                    var bmp = ToBitmap(bytes);
                    if (bmp != null) return bmp;
                }
                Log("Pfad1: Keine gültigen Bytes extrahiert.");
            }
            else Log("Pfad1: Keine GetMap/GetMapAsync Methode gefunden.");
        }
        catch (Exception ex)
        {
            Log("Pfad1 Fehler: " + ex.Message);
            // fallback
        }

        // -------- Pfad 2: Contracts über AppRequest/AppEmpty (Protobuf-Stil) --------
        try
        {
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
            var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));

            if (reqType != null && emptyType != null)
            {
                var req = Activator.CreateInstance(reqType)!;
                reqType.GetProperty("GetMap", System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                       ?.SetValue(req, Activator.CreateInstance(emptyType)!);

                var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
                if (send != null)
                {
                    var taskObj = send.Invoke(_api, new object[] { req });
                    object? resultObj = taskObj;

                    if (taskObj is Task t)
                    {
                        await t.ConfigureAwait(false);
                        resultObj = t.GetType().GetProperty("Result")?.GetValue(t);
                    }

                    var resp = GetProp(resultObj!, "Response") ?? resultObj;
                    var map = GetProp(resp, "Map") ?? resp;

                    var bytes = ReadBytesFlexible(map, "PngImage", "JpgImage", "Image", "Bytes", "Data");
                    if (bytes != null && LooksLikeImage(bytes))
                    {
                        Log($"Pfad2 OK: {bytes.Length} Bytes.");
                        var bmp = ToBitmap(bytes);
                        if (bmp != null) return bmp;
                    }
                    Log("Pfad2: Keine gültigen Bytes extrahiert.");
                }
                else Log("Pfad2: SendRequestAsync(req) nicht gefunden.");
            }
            else Log("Pfad2: AppRequest/AppEmpty nicht gefunden.");
        }
        catch (Exception ex)
        {
            Log("Pfad2 Fehler: " + ex.Message);
        }

        Log("Alle Map-Pfade ohne Erfolg → null.");
        return null;
    }
    // === NEW: strongly-typed shop record (lass ihn da, wo deine anderen Records sind)
    public sealed record ShopOrder
    {
        public int ItemId { get; init; }
        public int Quantity { get; init; }
        public int CurrencyItemId { get; init; }
        public int CurrencyAmount { get; init; }
        public int Stock { get; init; }
        public bool IsBlueprint { get; init; }

        // optional, falls die API Namen statt IDs mitgibt
        public string? ItemShortName { get; init; }
        public string? CurrencyShortName { get; init; }
    }

    public sealed record ShopMarker(uint Id, double X, double Y, string? Label)
    {
        // neu: Orders, damit dein MainWindow mit marker.Orders weiterläuft
        public List<ShopOrder> Orders { get; init; } = new();
    }

    // -------- reflection helpers (in der Klasse) --------
    private static object? Prop(object? o, string name)
        => o?.GetType().GetProperty(
               name,
               System.Reflection.BindingFlags.Instance |
               System.Reflection.BindingFlags.Public |
               System.Reflection.BindingFlags.IgnoreCase
           )?.GetValue(o);

    private static string? ReadString(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = Prop(o, n);
            if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static bool ReadBool(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = Prop(o, n);
            if (v is bool b) return b;
            if (v is int i) return i != 0;
            if (bool.TryParse(v?.ToString(), out var bb)) return bb;
        }
        return false;
    }

    private static int ReadInt(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = Prop(o, n);
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double d) return (int)Math.Round(d);
            if (int.TryParse(v?.ToString(), out var ii)) return ii;
        }
        return 0;
    }

    private static uint ReadUInt(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = Prop(o, n);
            if (v is uint u) return u;
            if (v is int i && i >= 0) return (uint)i;
            if (uint.TryParse(v?.ToString(), out var uu)) return uu;
            if (long.TryParse(v?.ToString(), out var ll) && ll >= 0) return (uint)ll;
        }
        return 0;
    }

    private static double ReadDouble(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = Prop(o, n);
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            if (double.TryParse(v?.ToString(), out var dd)) return dd;
        }
        return 0.0;
    }

    private static bool TryGetXY(object it, out double x, out double y)
    {
        var pos = Prop(it, "Position") ?? Prop(it, "Pos");
        x = ReadDouble(pos ?? it, "X", "x", "Lon", "Longitude");
        y = ReadDouble(pos ?? it, "Y", "y", "Lat", "Latitude");
        return true;
    }

    // NEW: dynamic marker bag
    public readonly struct DynMarker
    {
        public readonly uint Id;
        public readonly int Type;          // normierter Typ
        public readonly string Kind;
        public readonly double X;
        public readonly double Y;
        public readonly string? Label;     // Roh-Label vom Marker
        public readonly string? Name;      // Player-Name (falls vorhanden), sonst null
        public readonly ulong SteamId;     // NEU

        public DynMarker(uint id, int type, string kind, double x, double y, string? label, string? name, ulong steamId)
        {
            Id = id;
            Type = type;
            Kind = kind;
            X = x;
            Y = y;
            Label = label;
            Name = name;
            SteamId = steamId;             // NEU
        }
    }

    public sealed class TeamInfo
    {
        public sealed class Member
        {
            public ulong SteamId { get; set; }
            public string? Name { get; set; }
        }

        public List<Member> Members { get; } = new();
    }

    public async Task<TeamInfo?> GetTeamInfoAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        // lokale kleine Helfer (dupliziert für Unabhängigkeit)
        static object? RProp(object? o, string name) =>
            o?.GetType().GetProperty(name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase)
              ?.GetValue(o);

        static ulong RULong(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = RProp(o, n);
                if (v is ulong u) return u;
                if (v is long l && l >= 0) return (ulong)l;
                if (v is uint ui) return ui;
                if (ulong.TryParse(v?.ToString(), out var uu)) return uu;
            }
            return 0UL;
        }

        static string? RStr(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = RProp(o, n);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        try
        {
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
            var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));
            if (reqType == null || emptyType == null) return null;

            var req = Activator.CreateInstance(reqType)!;
            reqType.GetProperty("GetTeamInfo",
                    System.Reflection.BindingFlags.IgnoreCase |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public)
                ?.SetValue(req, Activator.CreateInstance(emptyType)!);

            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
            if (send == null) return null;

            var taskObj = send.Invoke(_api, new object[] { req });
            object? resp = taskObj;
            if (taskObj is Task tsk)
            {
                await tsk.ConfigureAwait(false);
                resp = tsk.GetType().GetProperty("Result")?.GetValue(tsk);
            }

            var r = RProp(resp, "Response") ?? resp;
            var ti = RProp(r, "TeamInfo") ?? r;
            var members = RProp(ti, "TeamInfo") ?? ti; // einige Builds verschachteln dies

            var list = (members as System.Collections.IEnumerable) ?? (RProp(members, "Members") as System.Collections.IEnumerable);
            if (list == null) return new TeamInfo();

            var result = new TeamInfo();
            foreach (var m in list)
            {
                if (m == null) continue;
                var sid = RULong(m, "SteamId", "SteamID", "UserId", "UserID");
                var name = RStr(m, "Name", "DisplayName", "PlayerName");
                result.Members.Add(new TeamInfo.Member { SteamId = sid, Name = name });
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<DynMarker>> GetDynamicMapMarkersAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        void L(string s) => _log?.Invoke("[dyn] " + s);

        // ---------- helpers (lokal, konfliktfrei benannt) ----------
        static object? RProp(object? o, string name)
            => o?.GetType().GetProperty(name,
                   System.Reflection.BindingFlags.Instance |
                   System.Reflection.BindingFlags.Public |
                   System.Reflection.BindingFlags.IgnoreCase)
                 ?.GetValue(o);

        static string? RStr(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = RProp(o, n);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        static int RInt(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = RProp(o, n);
                if (v is int i) return i;
                if (v is uint u) return unchecked((int)u);
                if (v is long l) return unchecked((int)l);
                if (v is short s) return s;
                if (v is byte b) return b;
                if (v != null && v.GetType().IsEnum) return Convert.ToInt32(v);
                if (int.TryParse(v?.ToString(), out var ii)) return ii;
            }
            return 0;
        }

        static uint RUInt(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = RProp(o, n);
                if (v is uint u) return u;
                if (v is int i && i >= 0) return (uint)i;
                if (v is long l && l >= 0) return (uint)l;
                if (uint.TryParse(v?.ToString(), out var uu)) return uu;
            }
            return 0u;
        }

        static ulong RULong(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = RProp(o, n);
                if (v is ulong u) return u;
                if (v is long l && l >= 0) return (ulong)l;
                if (v is uint ui) return ui;
                if (ulong.TryParse(v?.ToString(), out var uu)) return uu;
            }
            return 0UL;
        }

        static double RDbl(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = RProp(o, n);
                if (v is double d) return d;
                if (v is float f) return f;
                if (v is int i) return i;
                if (v is long l) return l;
                if (double.TryParse(v?.ToString(), out var dd)) return dd;
            }
            return 0.0;
        }

        static bool TryXY(object it, out double x, out double y)
        {
            var pos = RProp(it, "Position") ?? RProp(it, "Pos");
            x = RDbl(pos ?? it, "X", "x", "Lon", "Longitude");
            y = RDbl(pos ?? it, "Y", "y", "Lat", "Latitude");
            if (x == 0 && y == 0)
            {
                foreach (var p in it.GetType().GetProperties())
                {
                    var v = p.GetValue(it);
                    if (v == null || v is string) continue;
                    var px = v.GetType().GetProperty("X");
                    var py = v.GetType().GetProperty("Y");
                    if (px != null && py != null)
                    {
                        x = RDbl(v, "X");
                        y = RDbl(v, "Y");
                        break;
                    }
                }
            }
            return !(double.IsNaN(x) || double.IsNaN(y));
        }

        static bool LooksLikeShop(object it, int rawType)
        {
            // hard rule: MapMarker Type 3 ist VendingMachine => Shop
            if (rawType == 3) return true;
            var so = RProp(it, "SellOrders") ?? RProp(it, "Orders");
            if (so is System.Collections.IEnumerable en)
            {
                foreach (var _ in en) return true; // hat mind. 1 Order
            }
            // Manche Builds hängen Orders in Child "Vending"/"Sales"
            var vend = RProp(it, "Vending") ?? RProp(it, "Sales") ?? RProp(it, "Shop");
            var so2 = RProp(vend, "SellOrders") ?? RProp(vend, "Orders");
            if (so2 is System.Collections.IEnumerable en2)
            {
                foreach (var _ in en2) return true;
            }
            return false;
        }

        static (string kind, int norm) MapType(int rawType, string? label, string? typeName, ulong steamId)
        {
            // harte Matches
            if (rawType == 1) return ("Player", 1);
            if (rawType == 5) return ("Cargo Ship", 5);
            if (rawType == 6) return ("Travelling Vendor", 6);
            if (rawType == 4) return ("CH47", 4);
            if (rawType == 8) return ("Patrol Helicopter", 8);
            if (rawType == 9) return ("Travelling Vendor", 6); // alternative id
            if (rawType == 2) return ("Explosion", 2);

            // viele Server schicken die Kiste als GenericRadius
            // -> wenn Label/TypeName "crate/hack/locked" enthält: als Locked Crate behandeln
            var s = (label ?? "").ToLowerInvariant();
            var tn = (typeName ?? "").ToLowerInvariant();

            // Einige Implementierungen geben 0 + leer aus. Versuche dann den TypeName.
            bool looksLikeCrateToken =
                s.Contains("crate") || s.Contains("hack") || s.Contains("locked") ||
                tn.Contains("crate") || tn.Contains("hack") || tn.Contains("locked");

            if (rawType == 7 && looksLikeCrateToken) return ("Travelling Vendor", 6);
            if (rawType == 0 && looksLikeCrateToken) return ("Travelling Vendor", 6);

            // restliche Heuristik
            if (steamId != 0 || s.Contains("player") || tn.Contains("player")) return ("Player", 1);
            if (s.Contains("cargo") || tn.Contains("cargo")) return ("Cargo Ship", 5);
            if (s.Contains("patrol") || tn.Contains("patrol")) return ("Patrol Helicopter", 8);
            if (s.Contains("ch47") || s.Contains("chinook") || tn.Contains("ch47") || tn.Contains("chinook"))
                return ("CH47", 4);
            if (s.Contains("explosion") || s.Contains("debris") || tn.Contains("explosion") || tn.Contains("debris"))
                return ("explosion", 2);

            return ("Other", rawType != 0 ? rawType : 0);
        }
        // ------------------------------------------------------------

        var list = new List<DynMarker>();

        try
        {
            // Nur PATH B (roh, enum-sicher)
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
            var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));
            if (reqType == null || emptyType == null) return list;

            var req = Activator.CreateInstance(reqType)!;
            reqType.GetProperty("GetMapMarkers",
                    System.Reflection.BindingFlags.IgnoreCase |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public)
                ?.SetValue(req, Activator.CreateInstance(emptyType)!);

            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
            if (send == null) return list;

            var taskObj = send.Invoke(_api, new object[] { req });
            object? resp = taskObj;
            if (taskObj is Task tsk)
            {
                await tsk.ConfigureAwait(false);
                resp = tsk.GetType().GetProperty("Result")?.GetValue(tsk);
            }

            var r = RProp(resp, "Response") ?? resp;
            var mm = RProp(r, "MapMarkers") ?? r;

            // Primärliste: "Markers" (alle dynamischen, inkl. Player/Events)
            object? markers = RProp(mm, "Markers") ?? RProp(mm, "Marker");

            var pool = new List<object>();
            var seenLists = new HashSet<object>(ReferenceEqualityComparer.Instance); // dedup per Referenz

            void AddEnum(object? maybe)
            {
                if (maybe is System.Collections.IEnumerable en && maybe is not string)
                {
                    // Liste selbst deduplizieren (falls zweimal erreichbar)
                    if (!seenLists.Add(en)) return;
                    foreach (var it in en) if (it != null) pool.Add(it);
                }
            }

            // 1) Standard-Container
            if (markers != null) AddEnum(markers);

            // 2) explizite Crate-Container
            AddEnum(RProp(mm, "Crates"));
            AddEnum(RProp(mm, "HackableCrates"));
            AddEnum(RProp(mm, "LockedCrates"));

            // 3) generischer Fallback – bekannte Namen überspringen
            var skipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{ "Markers", "Marker", "Crates", "HackableCrates", "LockedCrates" };

            foreach (var p in (mm ?? r)!.GetType().GetProperties())
            {
                if (skipNames.Contains(p.Name)) continue;
                var v = p.GetValue(mm ?? r);
                AddEnum(v);
            }

            foreach (var it in pool)
            {
                var id = RUInt(it, "Id", "ID", "EntityId", "Identifier", "Uid", "UID", "MarkerId");
                var label = RStr(it, "Name", "Label", "Alias", "Token", "Note");
                var pname = RStr(it, "PlayerName", "DisplayName", "UserName", "SteamName");
                var rawType = RInt(it, "Type", "MarkerType", "TypeId", "TypeID", "type");
                var typeNm = it.GetType().Name;
                var steamId = RULong(it, "SteamId", "SteamID", "Steamid", "PlayerId", "UserId", "UserID");
                // Shops raushalten
                if (LooksLikeShop(it, rawType)) continue;

                if (!TryXY(it, out var x, out var y)) continue;

                

                // "Bottom-left-Ghost" von Nicht-Teams wegfiltern:
                // Wenn roh Player-artig, aber steamId==0 UND Label/Name leer UND sehr nah an 0/0 -> ignorieren
                if ((rawType == 1 || (label is null && pname is null)) && steamId == 0 &&
                    x < 10 && y < 10) continue;

                if (LooksLikeShop(it, rawType))
                {
                    // OPTIONAL: Falls du leere Shops in der Shop-Liste tracken willst,
                    // kannst du hier eine Übergabe an deine Vendors-Logik machen:
                    // TrackVendorMarker(it, x, y, label, rawType);
                    continue;
                }

                var (kind, norm) = MapType(rawType, label ?? pname, typeNm, steamId);

                list.Add(new DynMarker(id, norm, kind, x, y, label, pname ?? label, steamId));
            }
        }
        catch (Exception ex)
        {
            L("error: " + ex.Message);
        }

        return list;
    }

    sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static int MarkerTypeOf(object it)
    {
        // tries int Type first …
        var p = it.GetType().GetProperty("Type",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.IgnoreCase);

        var v = p?.GetValue(it);
        if (v is int i) return i;

        // … or string/enum-ish
        var s = v?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) return -1;

        if (int.TryParse(s, out var ii)) return ii;
        if (s.Contains("vending")) return 3;
        if (s.Contains("player")) return 1;
        if (s.Contains("cargo")) return 5;
        if (s.Contains("ch47") || s.Contains("chinook")) return 4; // some builds use 4
        if (s.Contains("patrol")) return 8;
        if (s.Contains("crate") || s.Contains("locked")) return 6;
        return -1;
    }

    public async Task<List<ShopMarker>> GetVendingShopsAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        void L(string s) => _log?.Invoke("[shops] " + s);

        // local helpers (you already have most of these in your file)
        static object? Prop(object? o, string name)
            => o?.GetType().GetProperty(name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase)?.GetValue(o);

        static string? ReadString(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = Prop(o, n);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        static int ReadInt(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = Prop(o, n);
                if (v is int i) return i;
                if (v is uint u) return unchecked((int)u);
                if (v is long l) return unchecked((int)l);
                if (int.TryParse(v?.ToString(), out var ii)) return ii;
            }
            return 0;
        }

        static uint ReadUInt(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = Prop(o, n);
                if (v is uint u) return u;
                if (v is int i && i >= 0) return (uint)i;
                if (v is long l && l >= 0) return (uint)l;
                if (uint.TryParse(v?.ToString(), out var uu)) return uu;
            }
            return 0u;
        }

        static double ReadDouble(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = Prop(o, n);
                if (v is double d) return d;
                if (v is float f) return f;
                if (v is int i) return i;
                if (v is long l) return l;
                if (double.TryParse(v?.ToString(), out var dd)) return dd;
            }
            return 0.0;
        }

        static bool TryGetXY(object it, out double x, out double y)
        {
            var pos = Prop(it, "Position") ?? Prop(it, "Pos");
            x = ReadDouble(pos ?? it, "X", "x", "Lon", "Longitude");
            y = ReadDouble(pos ?? it, "Y", "y", "Lat", "Latitude");

            if (x == 0 && y == 0)
            {
                foreach (var p in it.GetType().GetProperties())
                {
                    var v = p.GetValue(it);
                    if (v == null || v is string) continue;
                    var px = v.GetType().GetProperty("X");
                    var py = v.GetType().GetProperty("Y");
                    if (px != null && py != null)
                    {
                        x = ReadDouble(v, "X");
                        y = ReadDouble(v, "Y");
                        break;
                    }
                }
            }
            return !(double.IsNaN(x) || double.IsNaN(y));
        }

        // ---- parse SellOrders → ShopOrder
        static ShopOrder ParseOrder(object o) => new()
        {
            ItemId = ReadInt(o, "ItemId", "ItemID", "Itemid"),
            Quantity = ReadInt(o, "Quantity", "Amount", "Qty"),
            CurrencyItemId = ReadInt(o, "CurrencyItemId", "CurrencyId", "CurrencyID"),
            CurrencyAmount = ReadInt(o, "CurrencyAmount", "Price", "Cost", "CostPerItem"),
            Stock = ReadInt(o, "Stock", "AmountInStock", "Available"),
            IsBlueprint =
                ReadString(o, "IsBlueprint", "Blueprint")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
                ReadInt(o, "IsBlueprint", "Blueprint") != 0,
            ItemShortName = ReadString(o, "ItemShortName", "ShortName", "ItemName", "Item", "Name"),
            CurrencyShortName = ReadString(o, "CurrencyShortName", "CurrencyName", "Currency")
        };

        static bool LooksLikeOrdersLabel(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.ToLowerInvariant();
            return t.Contains("item#") || t.Contains("curr#") || t.Contains("→") || t.Contains(";") || t.Contains("stock");
        }

        static string? CleanLabel(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().Replace('\r', ' ').Replace('\n', ' ');
            if (LooksLikeOrdersLabel(s)) return null;
            if (s.Length > 48) s = s[..48] + "…";
            return s;
        }

        void ExtractFromCollection(object col, List<ShopMarker> outList)
        {
            if (col is not System.Collections.IEnumerable list) return;

            foreach (var it in list)
            {
                if (it is null) continue;

                // *** HARD FILTER: must really be a vending (type==3) ***
                var typeCode = MarkerTypeOf(it);
                if (typeCode != 3) continue;

                // must have some orders container at least
                var ordersObj = Prop(it, "SellOrders") ?? Prop(it, "Orders");
                if (ordersObj is null)
                {
                    var vend = Prop(it, "Vending") ?? Prop(it, "Sales") ?? Prop(it, "Shop");
                    ordersObj = Prop(vend, "SellOrders") ?? Prop(vend, "Orders");
                    if (ordersObj is null) return; // kein Shop → abbrechen (und NICHT outList.Add)
                }

                // coords
                if (!TryGetXY(it, out var x, out var y)) continue;

                // id + label
                uint id = ReadUInt(it, "Id", "ID", "EntityId", "VendingMachineId", "Identifier", "Uid", "UID");
                string? label = CleanLabel(ReadString(it, "Name", "Label", "Alias", "Token", "Note"));

                // materialize orders
                var orders = new List<ShopOrder>();
                if (ordersObj is System.Collections.IEnumerable en)
                {
                    foreach (var o in en) if (o != null) orders.Add(ParseOrder(o));
                }

                var marker = new ShopMarker(id, x, y, label) { Orders = orders };
                outList.Add(marker);
            }
        }

        var shops = new List<ShopMarker>();

        // PATH A – library (some builds throw unknown marker type)
        try
        {
            var t = _api.GetType();
            var m = t.GetMethod("GetMapMarkersAsync", new[] { typeof(CancellationToken) })
                 ?? t.GetMethod("GetMapMarkersAsync", Type.EmptyTypes)
                 ?? t.GetMethod("GetMapMarkers", Type.EmptyTypes);

            object? call = m == null ? null :
                (m.GetParameters().Length == 1 ? m.Invoke(_api, new object[] { ct }) : m.Invoke(_api, Array.Empty<object>()));

            object? result = call;
            if (call is Task task)
            {
                await task.ConfigureAwait(false);
                result = task.GetType().GetProperty("Result")?.GetValue(task);
            }

            var data = Prop(result, "Data") ?? result;

            // prefer explicit vending list if present
            var vend = Prop(data, "VendingMachines") ?? Prop(data, "Vending");
            if (vend != null) ExtractFromCollection(vend, shops);

            // otherwise generic scan – but still needs type==3 inside ExtractFromCollection
            if (shops.Count == 0 && data != null)
            {
                foreach (var p in data.GetType().GetProperties())
                {
                    var v = p.GetValue(data);
                    if (v is System.Collections.IEnumerable en && v is not string)
                        ExtractFromCollection(v, shops);
                }
            }
        }
        catch { /* ignore, fallback next */ }

        // PATH B – raw AppRequest (enum-agnostic)
        if (shops.Count == 0)
        {
            try
            {
                var asm = typeof(RustPlus).Assembly;
                var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
                var emptyTyp = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));
                if (reqType != null && emptyTyp != null)
                {
                    var req = Activator.CreateInstance(reqType)!;
                    reqType.GetProperty("GetMapMarkers",
                        System.Reflection.BindingFlags.IgnoreCase |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public)
                       ?.SetValue(req, Activator.CreateInstance(emptyTyp)!);

                    var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
                    if (send != null)
                    {
                        var taskObj = send.Invoke(_api, new object[] { req });
                        object? resp = taskObj;
                        if (taskObj is Task tsk)
                        {
                            await tsk.ConfigureAwait(false);
                            resp = tsk.GetType().GetProperty("Result")?.GetValue(tsk);
                        }

                        var r = Prop(resp, "Response") ?? resp;
                        var mm = Prop(r, "MapMarkers") ?? r;

                        var vend = Prop(mm, "VendingMachines") ?? Prop(mm, "Vending");
                        if (vend != null) ExtractFromCollection(vend, shops);
                        if (shops.Count == 0 && mm != null)
                        {
                            foreach (var p in mm.GetType().GetProperties())
                            {
                                var v = p.GetValue(mm);
                                if (v is System.Collections.IEnumerable en && v is not string)
                                    ExtractFromCollection(v, shops);
                            }
                        }
                    }
                }
            }
            catch { /* ignore */ }
        }

        return shops;
    }



    public sealed record ServerStatus(int Players, int MaxPlayers, int Queue, string TimeString);

    public async Task<ServerStatus?> GetServerStatusAsync(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        void L(string s) => _log?.Invoke("[status] " + s);

        // ---------------- helpers ----------------
        static object? Prop(object? o, string name)
            => o?.GetType().GetProperty(name,
                   System.Reflection.BindingFlags.Instance |
                   System.Reflection.BindingFlags.Public |
                   System.Reflection.BindingFlags.IgnoreCase)
                 ?.GetValue(o);

        static int? ReadIntCI(object? o, params string[] names)
        {
            if (o == null) return null;
            foreach (var n in names)
            {
                var v = Prop(o, n);
                if (v == null) continue;

                switch (v)
                {
                    case int ii: return ii;
                    case uint uu: return unchecked((int)uu);
                    case long ll: return unchecked((int)ll);
                    case double dd: return (int)Math.Round(dd);
                    case float ff: return (int)Math.Round(ff);
                    case string s:
                        // auch "123/200" o.ä. gracefully
                        var p = s.Split('/', ' ', '\t');
                        foreach (var tok in p)
                            if (int.TryParse(tok, out var num)) return num;
                        break;
                }
            }
            return null;
        }

        // liest HH:MM robust aus beliebigen Objekten/Feldern
        static bool TryReadTimeHHMM(object? o, out string hhmm, out string usedPath)
        {
            hhmm = ""; usedPath = "";
            if (o == null) return false;

            // --- helpers ---
            static string ToHHMM(int h, int m)
            {
                h = ((h % 24) + 24) % 24;
                m = ((m % 60) + 60) % 60;
                return $"{h:00}:{m:00}";
            }
            static int? AsInt(object? v) => v switch
            {
                int ii => ii,
                uint uu => unchecked((int)uu),
                long ll => unchecked((int)ll),
                double z => (int)Math.Round(z),
                float f => (int)Math.Round(f),
                string s => int.TryParse(s, out var n) ? n : null,
                _ => null
            };

            // Nur Namen zulassen, die wirklich nach Tageszeit klingen
            static bool LooksLikeTodName(string n)
            {
                var s = n.ToLowerInvariant();
                // exakt zulässig
                if (s == "time" || s == "daytime" || s == "clock" || s == "tod" || s == "gametime")
                    return true;
                // teilweise zulässig
                if (s.Contains("daytime") || s.Contains("clock"))
                    return true;
                return false;
            }

            // typische Fallen ausschließen
            static bool IsBlacklisted(string n)
            {
                var s = n.ToLowerInvariant();
                string[] bad = { "timescale", "timezone", "uptime", "realtime", "unscaled", "since", "until", "lifetime", "ping" };
                foreach (var b in bad) if (s.Contains(b)) return true;
                return false;
            }

            // 1) Hour/Minute direkt?
            var ho = o.GetType().GetProperty("Hour", System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetValue(o)
                  ?? o.GetType().GetProperty("Hours", System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetValue(o);
            var mo = o.GetType().GetProperty("Minute", System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetValue(o)
                  ?? o.GetType().GetProperty("Minutes", System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetValue(o);

            var h1 = AsInt(ho); var m1 = AsInt(mo);
            if (h1.HasValue && m1.HasValue) { hhmm = ToHHMM(h1.Value, m1.Value); usedPath = "Hour/Minute"; return true; }

            // 2) Zulässige "Time"-Felder (keine Blacklist) – numeric oder "HH:MM"
            foreach (var p in o.GetType().GetProperties())
            {
                var name = p.Name;
                if (!LooksLikeTodName(name) || IsBlacklisted(name)) continue;

                var v = p.GetValue(o);
                if (v == null) continue;

                // a) double/float 0..24 => Stunden.m
                if (v is double dz)
                {
                    if (dz >= 0 && dz < 24)
                    {
                        int h = (int)Math.Floor(dz);
                        int m = (int)Math.Round((dz - h) * 60);
                        if (m == 60) { h = (h + 1) % 24; m = 0; }
                        hhmm = ToHHMM(h, m); usedPath = name + " (double 0..24)"; return true;
                    }
                    if (dz >= 0 && dz <= 1440)
                    {
                        int h = (int)Math.Floor(dz / 60.0);
                        int m = (int)Math.Round(dz % 60.0);
                        hhmm = ToHHMM(h, m); usedPath = name + " (minutes 0..1440)"; return true;
                    }
                }
                else if (v is float fz)
                {
                    double dy = fz;
                    if (dy >= 0 && dy < 24)
                    {
                        int h = (int)Math.Floor(dy);
                        int m = (int)Math.Round((dy - h) * 60);
                        if (m == 60) { h = (h + 1) % 24; m = 0; }
                        hhmm = ToHHMM(h, m); usedPath = name + " (float 0..24)"; return true;
                    }
                }
                else if (v is string s && TimeSpan.TryParse(s, out var ts))
                {
                    hhmm = ToHHMM((int)ts.TotalHours % 24, ts.Minutes); usedPath = name + " (string HH:MM)"; return true;
                }
                else
                {
                    var mins = AsInt(v);
                    if (mins is int m && m >= 0 && m <= 1440)
                    {
                        hhmm = ToHHMM(m / 60, m % 60); usedPath = name + " (int minutes)"; return true;
                    }
                }
            }

            // 3) Rekursiv nur in sinnvolle Container (Time, Clock, Day…) absteigen
            foreach (var p in o.GetType().GetProperties())
            {
                var name = p.Name;
                if (IsBlacklisted(name)) continue;
                if (!(name.Contains("Time", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("Clock", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("Day", StringComparison.OrdinalIgnoreCase))) continue;

                var v = p.GetValue(o);
                if (v == null || v is string || v.GetType().IsPrimitive) continue;

                if (TryReadTimeHHMM(v, out hhmm, out var child))
                { usedPath = name + "." + child; return true; }
            }

            // 4) Fallback: Strings in Properties nach „HH:MM“ durchprobieren
            foreach (var p in o.GetType().GetProperties())
            {
                var sv = p.GetValue(o) as string;
                if (string.IsNullOrWhiteSpace(sv)) continue;
                var only = new string(sv.Where(ch => char.IsDigit(ch) || ch == ':').ToArray());
                if (TimeSpan.TryParse(only, out var ts2))
                {
                    hhmm = ToHHMM((int)ts2.TotalHours % 24, ts2.Minutes); usedPath = p.Name + " (string parsed)"; return true;
                }
            }

            return false;
        }

        // Zielwerte (werden Schritt für Schritt gefüllt)
        int players = -1, maxPlayers = -1, queue = -1;
        string timeStr = "";

        // ---------- PATH A: Bibliothek (GetInfoAsync / GetTimeAsync) ----------
        try
        {
            var t = _api.GetType();

            // GetInfoAsync
            var mInfo = t.GetMethod("GetInfoAsync", new[] { typeof(CancellationToken) })
                      ?? t.GetMethod("GetInfoAsync", Type.EmptyTypes);
            if (mInfo != null)
            {
                object? call = mInfo.GetParameters().Length == 1
                    ? mInfo.Invoke(_api, new object[] { ct })
                    : mInfo.Invoke(_api, Array.Empty<object>());

                if (call is Task task)
                {
                    await task.ConfigureAwait(false);
                    var res = task.GetType().GetProperty("Result")?.GetValue(task);
                    var data = Prop(res, "Data") ?? Prop(res, "Info") ?? res;

                    // viele mögliche Namen – wir nehmen den ersten Treffer
                    players = ReadIntCI(data, "Players", "PlayerCount", "Population", "Online", "CurrentPlayers") ?? players;
                    maxPlayers = ReadIntCI(data, "MaxPlayers", "MaxPopulation", "Slots", "Max") ?? maxPlayers;
                    queue = ReadIntCI(data, "Queue", "Queued", "QueuedPlayers", "QueuePlayers") ?? queue;
                    //L($"info(A): players={players} max={maxPlayers} queue={queue}");
                }
            }

            // GetTimeAsync
            var mTime = t.GetMethod("GetTimeAsync", new[] { typeof(CancellationToken) })
                     ?? t.GetMethod("GetTimeAsync", Type.EmptyTypes);
            if (mTime != null)
            {
                object? call = mTime.GetParameters().Length == 1
                    ? mTime.Invoke(_api, new object[] { ct })
                    : mTime.Invoke(_api, Array.Empty<object>());

                if (call is Task task)
                {
                    await task.ConfigureAwait(false);
                    var res = task.GetType().GetProperty("Result")?.GetValue(task);
                    var data = Prop(res, "Data") ?? Prop(res, "Time") ?? res;
                    if (TryReadTimeHHMM(data, out var tA, out var usedA)) { timeStr = tA; }// L($"time(A): {tA} via {usedA}"); }
                   // else L("time(A): (not found)");
                }
            }
        }
        catch (Exception ex)
        {
            L("pathA ignored: " + ex.Message);
        }

        // ---------- PATH B: Roh-Request (AppRequest.GetInfo / GetTime) ----------
        // Falls A nichts geliefert hat, holen wir roh – um Enums/Versionen der Lib zu umgehen.
        if (players < 0 || maxPlayers < 0 || string.IsNullOrEmpty(timeStr))
        {
            try
            {
                var asm = typeof(RustPlus).Assembly;
                var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
                var emptyTyp = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));
                if (reqType != null && emptyTyp != null)
                {
                    // --- Info ---
                    if (players < 0 || maxPlayers < 0 || queue < 0)
                    {
                        var reqInfo = Activator.CreateInstance(reqType)!;
                        reqType.GetProperty("GetInfo",
                            System.Reflection.BindingFlags.IgnoreCase |
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public)
                           ?.SetValue(reqInfo, Activator.CreateInstance(emptyTyp)!);

                        var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
                        if (send != null)
                        {
                            var taskObj = send.Invoke(_api, new object[] { reqInfo });
                            object? resp = taskObj;
                            if (taskObj is Task tsk) { await tsk.ConfigureAwait(false); resp = tsk.GetType().GetProperty("Result")?.GetValue(tsk); }

                            var r = Prop(resp, "Response") ?? resp;
                            var info = Prop(r, "Info") ?? r;

                            players = ReadIntCI(info, "Players", "PlayerCount", "Population", "Online", "CurrentPlayers") ?? players;
                            maxPlayers = ReadIntCI(info, "MaxPlayers", "MaxPopulation", "Slots", "Max") ?? maxPlayers;
                            queue = ReadIntCI(info, "Queue", "Queued", "QueuedPlayers", "QueuePlayers") ?? queue;
                            //L($"info(B): players={players} max={maxPlayers} queue={queue}");
                        }
                    }

                    // --- Time ---
                    if (string.IsNullOrEmpty(timeStr))
                    {
                        var reqTime = Activator.CreateInstance(reqType)!;
                        reqType.GetProperty("GetTime",
                            System.Reflection.BindingFlags.IgnoreCase |
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public)
                           ?.SetValue(reqTime, Activator.CreateInstance(emptyTyp)!);

                        var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
                        if (send != null)
                        {
                            var taskObj = send.Invoke(_api, new object[] { reqTime });
                            object? resp = taskObj;
                            if (taskObj is Task tsk) { await tsk.ConfigureAwait(false); resp = tsk.GetType().GetProperty("Result")?.GetValue(tsk); }

                            var r = Prop(resp, "Response") ?? resp;
                            var time = Prop(r, "Time") ?? r;
                            if (TryReadTimeHHMM(time, out var tB, out var usedB)) { timeStr = tB; }// L($"time(B): {tB} via {usedB}"); }
                            //else L("time(B): (not found)");
                        }
                    }
                }
            }
            catch (Exception exB)
            {
                L("pathB error: " + exB.Message);
            }
        }

        // Fallbacks glätten (lieber "–" als 0/0)
        var tStr = string.IsNullOrWhiteSpace(timeStr) ? "–" : timeStr;
        return new ServerStatus(players, maxPlayers, queue, tStr);
    }

    public Task SendTeamMessageAsync(string text, CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        var t = _api.GetType();

        var m = t.GetMethod("SendTeamMessageAsync", new[] { typeof(string), typeof(CancellationToken) }) ??
                t.GetMethod("SendTeamMessageAsync", new[] { typeof(string) }) ??
                t.GetMethod("SendTeamMessage", new[] { typeof(string) });

        if (m is null) throw new NotSupportedException("SendTeamMessage* nicht gefunden.");

        var args = m.GetParameters().Length == 2 ? new object[] { text, ct } : new object[] { text };
        var taskObj = m.Invoke(_api, args);
        return taskObj as Task ?? Task.CompletedTask;
    }

    private static bool? ReadBoolFlexible(object? src, params string[] names)
    {
        if (src == null) return null;

        // Direkt: bool oder bool?
        if (src is bool b0) return b0;
        var nb0 = src as bool?;
        if (nb0.HasValue) return nb0.Value;

        var t = src.GetType();

        // Lokale Hilfsfunktion: beliebigen Wert nach bool mappen
        static bool? ToBool(object? v)
        {
            if (v == null) return null;

            if (v is bool b1) return b1;
            var nb1 = v as bool?;
            if (nb1.HasValue) return nb1.Value;

            if (v is sbyte sb) return sb != 0;
            if (v is byte b2) return b2 != 0;
            if (v is short s1) return s1 != 0;
            if (v is ushort us1) return us1 != 0;
            if (v is int i1) return i1 != 0;
            if (v is uint ui1) return ui1 != 0;
            if (v is long l1) return l1 != 0;
            if (v is ulong ul1) return ul1 != 0UL;
            if (v is float f1) return Math.Abs(f1) > float.Epsilon;
            if (v is double d1) return Math.Abs(d1) > double.Epsilon;
            if (v is decimal m1) return m1 != 0m;

            var str = v as string;
            if (str != null)
            {
                if (bool.TryParse(str, out var bs)) return bs;
                if (int.TryParse(str, out var isInt)) return isInt != 0;
                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var dd))
                    return Math.Abs(dd) > double.Epsilon;
            }

            return null;
        }

        // 1) Bevorzugte Property-Namen probieren
        foreach (var n in names)
        {
            var p = t.GetProperty(n);
            if (p == null) continue;
            var v = p.GetValue(src);
            var bv = ToBool(v);
            if (bv.HasValue) return bv;
        }

        // 2) Irgendein bool-/nullable-bool-Property
        var bp = t.GetProperties().FirstOrDefault(p =>
            p.PropertyType == typeof(bool) || p.PropertyType == typeof(bool?));
        if (bp != null)
        {
            var bv = ToBool(bp.GetValue(src));
            if (bv.HasValue) return bv;
        }

        // 3) Notfalls irgendein simples/numerisches Property
        foreach (var p in t.GetProperties())
        {
            var pt = p.PropertyType;
            if (pt.IsPrimitive || pt == typeof(decimal) || pt == typeof(string))
            {
                var bv = ToBool(p.GetValue(src));
                if (bv.HasValue) return bv;
            }
        }

        return null;
    }

    private static (string? kind, bool? val) DecodeEntityInfo(object ent)
    {
        // Art bestimmen
        var typeStr = ReadProp<object>(ent, "Type", "EntityType", "EntType")?.ToString();
        string? kind = null;
        if (!string.IsNullOrWhiteSpace(typeStr))
        {
            if (typeStr.Contains("Alarm", StringComparison.OrdinalIgnoreCase)) kind = "SmartAlarm";
            else if (typeStr.Contains("Switch", StringComparison.OrdinalIgnoreCase)) kind = "SmartSwitch";
        }

        // --- SMART ALARM: nur "Power"-Eigenschaften zulassen ----------------------
        if (string.Equals(kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
        {
            // Wichtig: KEIN "IsActive", "Enabled" oder "Value" – die sind häufig dauerhaft true.
            string[] powerNames = { "IsPowered", "HasPower", "PowerOn", "Powered", "HasElectricity", "IsOn", "On" };

            // 1) direkt am Objekt
            var v = ReadBoolFlexible(ent, powerNames);
            if (v != null) return (kind, v);

            // 2) in Subobjekten, aber ebenfalls nur die Power-Namen zulassen
            foreach (var p in ent.GetType().GetProperties())
            {
                if (!p.PropertyType.IsValueType && p.PropertyType != typeof(string))
                {
                    var sub = p.GetValue(ent);
                    var sv = ReadBoolFlexible(sub, powerNames);
                    if (sv != null) return (kind, sv);
                }
            }

            // Keine passende Info gefunden
            return (kind, null);
        }

        // --- Standard-Fall (SmartSwitch & Co.): bisherige generische Logik -------
        // bevorzugte Namen zuerst
        var preferred = new[] { "IsOn", "On", "Value", "Active", "Enabled", "IsActive", "PowerOn" };

        // 1) direkt am Objekt
        var direct = ReadBoolFlexible(ent, preferred);
        if (direct != null) return (kind, direct);

        // 2) in Unterobjekten (bevorzugte Namen zuerst)
        foreach (var p in ent.GetType().GetProperties())
        {
            if (p.PropertyType == typeof(bool)) return (kind, (bool?)p.GetValue(ent));
            if (!p.PropertyType.IsValueType && p.PropertyType != typeof(string))
            {
                var sub = p.GetValue(ent);
                var sv = ReadBoolFlexible(sub, preferred);
                if (sv != null) return (kind, sv);
            }
        }

        return (kind, null);
    }


    // 2.3 Öffentliche Probe-API – nutzt erst "neu", dann Contracts
    // Bequeme Overload (optional, falls du an vielen Stellen ohne CT aufrufst)
    public Task<EntityProbeResult> ProbeEntityAsync(uint entityId)
    => ProbeEntityAsync(entityId, CancellationToken.None);

    // DIE Interface-Methode:
    public async Task<EntityProbeResult> ProbeEntityAsync(uint entityId, CancellationToken ct)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        ct.ThrowIfCancellationRequested();

        // 0) Spezialfall Switch (bleibt wie gehabt, bringt zugleich das Event-Abo)
        try
        {
            var sw = await _api.GetSmartSwitchInfoAsync(entityId);
            if (sw?.IsSuccess == true)
                return new EntityProbeResult(true, "SmartSwitch", sw.Data!.IsActive);
        }
        catch { /* ok */ }

        // 1) Neue API: GetEntityInfoAsync(uint[,CT])
        // 1) Neue API: GetEntityInfoAsync(uint[,CT])
        try
        {
            var t = _api.GetType();
            var m = t.GetMethod("GetEntityInfoAsync", new[] { typeof(uint), typeof(CancellationToken) })
                  ?? t.GetMethod("GetEntityInfoAsync", new[] { typeof(uint) });
            if (m != null)
            {
                object? call = (m.GetParameters().Length == 2)
                    ? m.Invoke(_api, new object[] { entityId, ct })
                    : m.Invoke(_api, new object[] { entityId });

                if (call is Task task)
                {
                    await task;
                    var ok = TryGetTaskResultSuccess(task);
                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                    var data = result?.GetType().GetProperty("Data")?.GetValue(result);
                    if (ok == true && data != null)
                    {
                        var (kind, val) = DecodeEntityInfo(data);   // <— WICHTIG
                        return new EntityProbeResult(true, kind, val);
                    }
                }
            }
        }
        catch { /* egal – wir fallen auf Contracts zurück */ }

        // 2) Contracts via aktuelle API (AppRequest/GetEntityInfo)
        try
        {
            var asm = typeof(RustPlus).Assembly;

            var reqType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppRequest");
            var emptyType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppEmpty");
            if (reqType == null || emptyType == null) return new EntityProbeResult(false, null, null);

            var req = Activator.CreateInstance(reqType)!;
            var empty = Activator.CreateInstance(emptyType)!;

            reqType.GetProperty("EntityId")?.SetValue(req, entityId);
            reqType.GetProperty("GetEntityInfo")?.SetValue(req, empty);

            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
            if (send == null) return new EntityProbeResult(false, null, null);

            var taskObj = send.Invoke(_api, new object[] { req });
            if (taskObj is not Task task) return new EntityProbeResult(false, null, null);

            await task; // kurzer Timeout ist durch CT abgedeckt

            var result = task.GetType().GetProperty("Result")?.GetValue(task);
            var resp = result?.GetType().GetProperty("Response")?.GetValue(result) ?? result;
            var ent = resp?.GetType().GetProperty("EntityInfo")?.GetValue(resp);

            if (ent != null)
            {
                var (kind, val) = DecodeEntityInfo(ent);
                return new EntityProbeResult(true, kind, val);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log("ProbeEntity (contracts) Fehler: " + ex.Message); }

        return new EntityProbeResult(false, null, null);
    }

    public async Task PrimeSubscriptionsAsync(IEnumerable<uint> entityIds, CancellationToken ct = default)
    {
        if (_api is null) return;
        foreach (var id in entityIds.Distinct())
        {
            try
            {
                // versuche "neue" API
                var _ = await _api.GetSmartSwitchInfoAsync(id);
            }
            catch
            {
                // egal – Hauptsache einmal „kontakt“ gehabt
            }
            await Task.Delay(50, ct);
        }
    }

    public async Task ConnectAsync(ServerProfile profile, CancellationToken ct)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (!ulong.TryParse(profile.SteamId64, out var steamId))
            throw new ArgumentException("Ungültige SteamID64.", nameof(profile));
        if (!int.TryParse(profile.PlayerToken, out var playerToken))
            throw new ArgumentException("Ungültiger PlayerToken.", nameof(profile));

        _host = profile.Host;
        _port = profile.Port;
        _steamId = steamId;
        _playerToken = playerToken;

        async Task<(bool ok, string? err)> TryAsync(bool useProxy)
        {
            _api = new RustPlus(profile.Host, profile.Port, steamId, playerToken, useProxy);

            // optionales ConnectAsync aufrufen, falls vorhanden
            try
            {
                var mConnect = _api.GetType().GetMethod("ConnectAsync", new[] { typeof(CancellationToken) })
                             ?? _api.GetType().GetMethod("ConnectAsync", Type.EmptyTypes);
                if (mConnect != null)
                {
                    var res = mConnect.GetParameters().Length == 1
                        ? mConnect.Invoke(_api, new object[] { ct })
                        : mConnect.Invoke(_api, Array.Empty<object>());
                    if (res is Task t) await t;
                }
            }
            catch (Exception ex) { _log("ConnectAsync-Call schlug fehl: " + ex.Message); }

            // “Kontakt” prüfen
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));

                var infoTask = _api.GetInfoAsync();
                var done = await Task.WhenAny(infoTask, Task.Delay(7000, cts.Token));
                if (done != infoTask) return (false, "Timeout");

                var info = infoTask.Result;
                if (info?.IsSuccess == true)
                {
                    _log($"Authentifiziert – {(useProxy ? "über Facepunch-Proxy" : "direkt")}.");
                    return (true, null);
                }
                return (false, info?.Error?.Message ?? "keine Antwort / Fehler");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        var first = profile.UseFacepunchProxy;

        var (ok1, err1) = await TryAsync(first);
        if (ok1)
        {
            _useProxyCurrent = first;
            HookEventsIfNeeded();     // <— HIER
            return;
        }

        var (ok2, err2) = await TryAsync(!first);
        if (ok2)
        {
            _useProxyCurrent = !first;
            HookEventsIfNeeded();     // <— UND HIER
            return;
        }

        _log($"GetInfo (Pfad1: {(first ? "Proxy" : "Direkt")}): {err1}");
        _log($"GetInfo (Pfad2: {(!first ? "Proxy" : "Direkt")}): {err2}");
        throw new InvalidOperationException("Rust+ nicht erreichbar (direkt & Proxy).");
    }

    public async Task DisconnectAsync()
    {
        try { if (_api is not null) await _api.DisconnectAsync(); }
        catch { }
        finally { _api = null; _eventsHooked = false; }
        _log("Verbindung getrennt.");
    }

    // Minimaler „Basics“-Abruf (wahlfrei; nur Log)
    public async Task FetchBasicsAsync()
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        var info = await _api.GetInfoAsync();
        _log(info?.IsSuccess == true ? "Serverinfo: OK" : $"Serverinfo: Fehler: {info?.Error?.Message}");

        var time = await _api.GetTimeAsync();
        _log(time?.IsSuccess == true ? "Zeit abgefragt." : $"Zeit: Fehler: {time?.Error?.Message}");

        var team = await _api.GetTeamInfoAsync();
        _log(team?.IsSuccess == true ? "Teaminfo abgefragt." : $"Teaminfo: Fehler: {team?.Error?.Message}");
    }


    // ---- Helper: Timeout-Wrapper ums Legacy-Senden
    private async Task<(bool ok, string? err)> TrySetViaLegacyWithResultAsync_Timeout(uint id, bool on, int timeoutMs)
    {
        var work = TrySetViaLegacyWithResultAsync(id, on);
        var delay = Task.Delay(timeoutMs);
        var done = await Task.WhenAny(work, delay);
        return done == work ? await work : (false, "Timeout");
    }



   

#pragma warning disable 618
    private async Task<(bool ok, string? err)> TrySetViaLegacyWithResultAsync(uint entityId, bool on)
    {
        try
        {
            if (_host is null) return (false, "keine Verbindung");

            var legacy = new RustPlusLegacy(_host, _port, _steamId, _playerToken, _useProxyCurrent);

            var mConn = legacy.GetType().GetMethod("ConnectAsync", Type.EmptyTypes)
                       ?? legacy.GetType().GetMethod("ConnectAsync", new[] { typeof(CancellationToken) });
            if (mConn != null)
            {
                var r = mConn.GetParameters().Length == 0
                    ? mConn.Invoke(legacy, Array.Empty<object>())
                    : mConn.Invoke(legacy, new object[] { CancellationToken.None });
                if (r is Task t) await t;
            }

            var asm = typeof(RustPlusLegacy).Assembly;

            var appRequestType =
                asm.GetTypes().FirstOrDefault(t => t.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase)) ??
                asm.GetTypes().FirstOrDefault(t => t.Name.EndsWith("AppRequest", StringComparison.OrdinalIgnoreCase));
            var actionType = asm.GetTypes().FirstOrDefault(t => t.Name.IndexOf("SetEntityValue", StringComparison.OrdinalIgnoreCase) >= 0);

            if (appRequestType == null || actionType == null)
            {
                DumpLegacyShapeOnce();
                return (false, "Legacy-Typen nicht gefunden (AppRequest/AppSetEntityValue)");
            }

            var req = Activator.CreateInstance(appRequestType)!;
            var action = Activator.CreateInstance(actionType)!;

            // >>> In DEINER Version sitzt EntityId auf dem REQUEST
            var idOnReq = FindNumericIdMember(appRequestType);
            if (idOnReq.prop == null && idOnReq.field == null)
            {
                DumpLegacyShapeOnce();
                return (false, "Legacy-EntityId-Member nicht gefunden");
            }
            SetMember(req, idOnReq, entityId);

            // Bool (Value) auf der Action setzen
            var boolMember = FindBoolMember(actionType);
            if (boolMember.prop == null && boolMember.field == null)
            {
                DumpLegacyShapeOnce();
                return (false, "Legacy-Bool-Member nicht gefunden");
            }
            SetMember(action, boolMember, on);

            // Bonus: PlayerId/PlayerToken am Request setzen (falls vorhanden)
            appRequestType.GetProperty("PlayerId")?.SetValue(req, _steamId);
            appRequestType.GetProperty("PlayerToken")?.SetValue(req, _playerToken);

            // Action in Request hängen (Property „SetEntityValue“)
            var attachProp = appRequestType.GetProperties().FirstOrDefault(p =>
                p.PropertyType == actionType ||
                p.PropertyType.IsAssignableFrom(actionType) ||
                p.Name.IndexOf("SetEntityValue", StringComparison.OrdinalIgnoreCase) >= 0);
            if (attachProp == null)
            {
                DumpLegacyShapeOnce();
                return (false, "Legacy-Request-Property zum Anhängen der Action nicht gefunden");
            }
            attachProp.SetValue(req, action);

            // senden
            var mSend = legacy.GetType().GetMethod("SendRequestAsync", new[] { appRequestType });
            if (mSend == null) return (false, "SendRequestAsync nicht gefunden");

            var sendObj = mSend.Invoke(legacy, new object[] { req });
            if (sendObj is not Task sendTask) return (false, "SendRequestAsync Rückgabewert kein Task");
            await sendTask;

            // --- ACK auswerten: Success ist ein Objekt (AppSuccess), nicht bool ---
            bool? ok = null; string? msg = null;
            try
            {
                var resultProp = sendTask.GetType().GetProperty("Result");
                var result = resultProp?.GetValue(sendTask);
                var resp = result?.GetType().GetProperty("Response")?.GetValue(result)
                        ?? result?.GetType().GetProperty("AppResponse")?.GetValue(result)
                        ?? result;

                if (resp != null)
                {
                    var successProp = resp.GetType().GetProperty("Success");
                    var successVal = successProp?.GetValue(resp);

                    if (successVal is bool b) ok = b; // (für manche Builds)
                    else if (successVal != null)
                    {
                        // AppSuccess-Objekt: versuche .Ok / .Success / .Value
                        var okProp = successVal.GetType().GetProperty("Ok")
                                  ?? successVal.GetType().GetProperty("Success")
                                  ?? successVal.GetType().GetProperty("Value");
                        if (okProp?.GetValue(successVal) is bool bb) ok = bb;
                        else ok = true; // Presence von Success => als OK werten
                    }

                    var errProp = resp.GetType().GetProperty("Error") ?? resp.GetType().GetProperty("ErrorInfo");
                    var errObj = errProp?.GetValue(resp);
                    var msgProp = errObj?.GetType().GetProperty("Message") ?? errObj?.GetType().GetProperty("ErrorMessage");
                    msg = msgProp?.GetValue(errObj) as string;
                }
            }
            catch { /* tolerant */ }

            try { legacy.GetType().GetMethod("DisconnectAsync")?.Invoke(legacy, Array.Empty<object>()); } catch { }

            return (ok == true ? (true, null) : (false, msg ?? "Server hat nicht bestätigt"));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
#pragma warning restore 618
    // exakt zur Interface-Signatur
    public async Task ToggleSmartSwitchAsync(long entityId, bool on, CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        var id = (uint)entityId;

        // Sende EINEN Weg (mit Fallbacks), dann warte auf Bestätigung.
        bool sent = false;

        // a) Explizite neue Methoden
        if (await TryToggleExplicitAsync(id, on, ct)) { sent = true; }
        else
        {
            // b) SetEntityValue*-Kompat
            var compat = await TrySetEntityValueCompatAsync(id, on);
            if (compat == true) sent = true;
            else
            {
                // c) Contracts (liefert oft kein ACK) – trotzdem probieren
                var (ok3, _) = await TrySendContractsViaCurrentApiAsync(id, on, RequestTimeoutMs);
                if (!ok3)
                {
                    // d) Legacy-Fallback
                    var (okLegacy, _) = await TrySetViaLegacyWithResultAsync_Timeout(id, on, RequestTimeoutMs);
                    sent = okLegacy;
                }
                else sent = true;
            }
        }

        // Egal ob ACK kam: per Event/Poll verifizieren
        var confirmed = await WaitForSwitchStateAsync(id, on, 3000, ct);
        if (confirmed)
            _log($"SmartSwitch {id}: State confirmed → {(on ? "ON" : "OFF")}.");
        else
            _log($"Smart Device {id}: Switching failed – Timeout (no confirmation or not a Smart Switch).");
    }





    // ---- (unverändert lassen) VerifyStateAsync wie bei dir, aber etwas längere Wartezeit
    private async Task VerifyStateAsync(uint id, bool expected)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // korrekt typisierter Handler für das API-Event
        EventHandler<SmartSwitchEventArg>? handler = null;
        handler = (sender, sw) =>
        {
            try
            {
                var sid = GetEntityId(sw);
                var on = GetIsActive(sw);
                if (sid == id && on == expected)
                {
                    tcs.TrySetResult(true);
                }
            }
            catch { /* ignore */ }
        };

        _api!.OnSmartSwitchTriggered += handler;
        try
        {
            // kleiner Polling-Fallback, falls kein Event kommt
            var deadline = DateTime.UtcNow.AddSeconds(2.5);
            while (DateTime.UtcNow < deadline && !tcs.Task.IsCompleted)
            {
                await Task.Delay(200);
                try
                {
                    var after = await _api.GetSmartSwitchInfoAsync(id);
                    if (after?.IsSuccess == true && after.Data != null && after.Data.IsActive == expected)
                    {
                        tcs.TrySetResult(true);
                        break;
                    }
                }
                catch { /* ok */ }
            }

            var success = tcs.Task.IsCompleted && tcs.Task.Result;
            if (success)
                _log($"SmartSwitch {id}: Zustand bestätigt → {(expected ? "AN" : "AUS")}.");
            else
                _log($"SmartSwitch {id}: Server hat Zustand NICHT geändert.");
        }
        finally
        {
            try { if (handler != null) _api.OnSmartSwitchTriggered -= handler; } catch { }
        }
    }


    // ---- (leicht erweitert) TryToggleExplicitAsync: zusätzlich Timeout + klarere Logs
    private static bool ParamIsEntityId(Type t) =>
     t == typeof(uint) || t == typeof(int) || t == typeof(long) ||
     t == typeof(UInt32) || t == typeof(Int32) || t == typeof(Int64);

   

    private static (PropertyInfo? prop, FieldInfo? field) FindNumericIdMember(Type t)
    {
        // 1) bevorzugte Namen
        var p = t.GetProperties().FirstOrDefault(x =>
            ParamIsEntityId(x.PropertyType) &&
           (x.Name.Equals("EntityId", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("EntityID", StringComparison.OrdinalIgnoreCase)));
        if (p != null) return (p, null);

        // 2) beliebige *Id*-Property mit Zahlentyp
        p = t.GetProperties().FirstOrDefault(x =>
            ParamIsEntityId(x.PropertyType) &&
            x.Name.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0);
        if (p != null) return (p, null);

        // 3) FIELDS: bevorzugte Namen
        var f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 .FirstOrDefault(x => ParamIsEntityId(x.FieldType) &&
                     (x.Name.Equals("EntityId", StringComparison.OrdinalIgnoreCase) ||
                      x.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                      x.Name.Equals("EntityID", StringComparison.OrdinalIgnoreCase)));
        if (f != null) return (null, f);

        // 4) FIELD mit *id*
        f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             .FirstOrDefault(x => ParamIsEntityId(x.FieldType) &&
                  x.Name.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0);
        return (null, f);
    }

    private static (PropertyInfo? prop, FieldInfo? field) FindBoolMember(Type t)
    {
        // Bevorzugte Namen
        var p = t.GetProperties().FirstOrDefault(x =>
            x.PropertyType == typeof(bool) &&
           (x.Name.Equals("TurnOn", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("On", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Equals("IsOn", StringComparison.OrdinalIgnoreCase)));
        if (p != null) return (p, null);

        // irgendein Bool
        p = t.GetProperties().FirstOrDefault(x => x.PropertyType == typeof(bool));
        if (p != null) return (p, null);

        var f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 .FirstOrDefault(x => x.FieldType == typeof(bool) &&
                    (x.Name.IndexOf("on", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     x.Name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     x.Name.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0));
        if (f != null) return (null, f);

        f = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             .FirstOrDefault(x => x.FieldType == typeof(bool));
        return (null, f);
    }

    private static void SetMember(object target, (PropertyInfo? prop, FieldInfo? field) m, object val)
    {
        if (m.prop != null) { var v = Convert.ChangeType(val, m.prop.PropertyType); m.prop.SetValue(target, v); return; }
        if (m.field != null) { var v = Convert.ChangeType(val, m.field.FieldType); m.field.SetValue(target, v); return; }
        throw new InvalidOperationException("Member zum Setzen nicht gefunden.");
    }

    private void DumpLegacyShapeOnce()
    {
        if (_dumpedLegacy) return;
        _dumpedLegacy = true;

        var asm = typeof(RustPlusLegacy).Assembly;
        var names = new[] { "AppRequest", "AppTurnSmartSwitch", "AppSetEntityValue", "AppResponse", "AppMessage" };
        foreach (var ty in asm.GetTypes().Where(t => names.Any(n => t.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)))
        {
            _log($"[legacy-type] {ty.FullName}");
            foreach (var p in ty.GetProperties()) _log($"  prop  {p.PropertyType.Name} {p.Name}");
            foreach (var f in ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                _log($"  field {f.FieldType.Name} {f.Name}");
        }
    }
    private bool _dumpedLegacy = false;

    private async Task<bool> TryToggleExplicitAsync(uint id, bool on, CancellationToken ct)
    {
        var t = _api!.GetType();

        // Kandidaten: Toggle*, TurnSmartSwitchOn/Off*, jeweils evtl. mit "Legacy" und optionalem CancellationToken
        var methods = t.GetMethods()
            .Where(m =>
            {
                var n = m.Name.ToLowerInvariant();
                if (!(n.Contains("togglesmartswitch") || n.Contains("turnsmartswitchon") || n.Contains("turnsmartswitchoff")))
                    return false;
                var p = m.GetParameters();
                if (p.Length < 1 || !ParamIsEntityId(p[0].ParameterType)) return false;
                // bool nur bei Toggle; bei On/Off nicht nötig
                if (n.Contains("toggle"))
                {
                    if (p.Length < 2 || p[1].ParameterType != typeof(bool)) return false;
                }
                return true;
            })
            .ToList();

        foreach (var m in methods)
        {
            try
            {
                var pars = m.GetParameters();
                object[] args;
                if (m.Name.Contains("Toggle", StringComparison.OrdinalIgnoreCase))
                    args = (pars.Length >= 3 && pars[2].ParameterType == typeof(CancellationToken))
                        ? new object[] { Convert.ChangeType(id, pars[0].ParameterType), on, ct }
                        : new object[] { Convert.ChangeType(id, pars[0].ParameterType), on };
                else
                    args = (pars.Length >= 2 && pars.Last().ParameterType == typeof(CancellationToken))
                        ? new object[] { Convert.ChangeType(id, pars[0].ParameterType), ct }
                        : new object[] { Convert.ChangeType(id, pars[0].ParameterType) };

                var res = m.Invoke(_api, args);
                if (res is Task task)
                {
                    await task;
                    var ok = TryGetTaskResultSuccess(task);
                    if (ok.HasValue) return ok.Value;
                    return true; // Task lief ohne Exception → vermutlich ok
                }
            }
            catch (Exception ex) { _log("ToggleSmartSwitch*-Aufruf fehlgeschlagen: " + ex.Message); }
        }

        _log("Pfad: ToggleSmartSwitch* (neu) ✗");
        return false;
    }

    private bool? TryGetTaskResultSuccess(Task? task)
    {
        if (task == null) return null;
        try
        {
            var tt = task.GetType();
            var hasResult = tt.IsGenericType;
            if (!hasResult) return null;

            var resultProp = tt.GetProperty("Result");
            var result = resultProp?.GetValue(task);
            if (result == null) return null;

            var isSuccessProp = result.GetType().GetProperty("IsSuccess");
            if (isSuccessProp?.GetValue(result) is bool b) return b;

            var errorProp = result.GetType().GetProperty("Error");
            var msgProp = errorProp?.PropertyType.GetProperty("Message");
            var msg = msgProp?.GetValue(errorProp?.GetValue(result)) as string;
            if (!string.IsNullOrWhiteSpace(msg)) _log("API-Error: " + msg);
            return null;
        }
        catch { return null; }
    }

    // ---- Anpassung: TrySetEntityValueCompatAsync gibt jetzt bool? (null = Methode fehlt)
    private async Task<bool?> TrySetEntityValueCompatAsync(uint entityId, bool on)
    {
        var t = _api!.GetType();

        var candidates = t.GetMethods()
            .Where(m =>
            {
                if (!m.Name.Contains("SetEntityValue", StringComparison.OrdinalIgnoreCase)) return false;
                var p = m.GetParameters();
                if (p.Length < 2) return false;
                if (!ParamIsEntityId(p[0].ParameterType)) return false;
                if (p[1].ParameterType != typeof(bool)) return false;
                return true;
            })
            .ToList();

        foreach (var m in candidates)
        {
            try
            {
                var p = m.GetParameters();
                var args = (p.Length >= 3 && p[2].ParameterType == typeof(CancellationToken))
                    ? new object[] { Convert.ChangeType(entityId, p[0].ParameterType), on, CancellationToken.None }
                    : new object[] { Convert.ChangeType(entityId, p[0].ParameterType), on };

                var res = m.Invoke(_api, args);
                if (res is Task task)
                {
                    await task;
                    var ok = TryGetTaskResultSuccess(task);
                    return ok ?? true; // wenn kein IsSuccess → trotzdem als „gesendet“ werten
                }
                return true;
            }
            catch (Exception ex)
            {
                _log("SetEntityValue*-Aufruf fehlgeschlagen: " + ex.Message);
                return false;
            }
        }

        return null; // keine passende Methode vorhanden
    }

    // Falls dein Interface das verlangt – aktuell No-Op (wir hören Alarme über den FCM-Prozess)

    private async Task<(bool ok, string? err)> TrySendContractsViaCurrentApiAsync(
    uint entityId, bool on, int timeoutMs = 2000)
    {
        try
        {
            var asm = typeof(RustPlus).Assembly;

            var appRequestType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppRequest");
            var appSetType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppSetEntityValue");
            if (appRequestType == null || appSetType == null)
                return (false, "Contracts (AppRequest/AppSetEntityValue) nicht gefunden");

            // Instanzen
            var req = Activator.CreateInstance(appRequestType)!;
            var set = Activator.CreateInstance(appSetType)!;

            // WICHTIG: EntityId am *Request* setzen (nicht im Set-Objekt)
            appRequestType.GetProperty("EntityId")?.SetValue(req, entityId);

            // Den Wert im Set-Objekt setzen und an Request anhängen
            var pValue = appSetType.GetProperty("Value");
            if (pValue == null) return (false, "AppSetEntityValue.Value fehlt");
            pValue.SetValue(set, on);
            appRequestType.GetProperty("SetEntityValue")?.SetValue(req, set);

            // Senden über die aktuelle RustPlus-Instanz
            var send = _api!.GetType().GetMethod("SendRequestAsync", new[] { appRequestType });
            if (send == null) return (false, "SendRequestAsync nicht vorhanden");

            var taskObj = send.Invoke(_api, new object[] { req });
            if (taskObj is not Task task) return (false, "SendRequestAsync lieferte keinen Task");

            var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (done != task) return (false, "timeout");

            // Falls das Resultat ein IsSuccess hat, auswerten; sonst „unklar“ (null)
            var ok = TryGetTaskResultSuccess(task);
            return (ok ?? false, ok is null ? "keine Success-Info" : null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
    public Task SubscribeRaidAlarmsAsync(CancellationToken ct = default)
    {
        _log("SubscribeRaidAlarms: wird über den FCM-Listener gehandhabt (kein zusätzlicher WS-Subscribe nötig).");
        return Task.CompletedTask;
    }

    public void Dispose() => _ = DisconnectAsync();

    public async Task<bool?> GetSmartSwitchStateAsync(uint entityId)
    {
        if (_api is null) return null;
        try
        {
            var res = await _api.GetSmartSwitchInfoAsync(entityId);
            if (res != null)
            {
                // Response<T> oder direktes Objekt tolerieren
                var rt = res.GetType();
                var data = rt.GetProperty("Data")?.GetValue(res) ?? res;
                if (data != null)
                {
                    var dt = data.GetType();
                    var p = dt.GetProperty("IsActive")
                          ?? dt.GetProperty("IsOn")
                          ?? dt.GetProperty("Active")
                          ?? dt.GetProperty("value");

                    if (p != null && p.PropertyType == typeof(bool))
                        return (bool)p.GetValue(data)!;
                }
            }
        }
        catch (Exception ex)
        {
            var msg = ex.Message ?? string.Empty;

            // Häufige Nicht-Switch-Muster → stillschweigend ignorieren
            if (msg.IndexOf("not a SmartSwitch", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (msg.IndexOf("SmartSwitchInfo", StringComparison.OrdinalIgnoreCase) >= 0 &&
                msg.IndexOf("does not contain a definition", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (msg.IndexOf("IsActive", StringComparison.OrdinalIgnoreCase) >= 0 &&
                msg.IndexOf("does not contain a definition", StringComparison.OrdinalIgnoreCase) >= 0) return null;

            // Unbekanntes Problem -> einmal loggen
            _log("GetSmartSwitchStateAsync: " + msg);
        }
        return null;
    }

    private async Task<bool> WaitForSwitchStateAsync(uint id, bool desired, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(uint eid, bool isOn, string? kind)
        {
            if (eid == id && isOn == desired) tcs.TrySetResult(true);
        }

        DeviceStateEvent += Handler;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested && !tcs.Task.IsCompleted)
                {
                    var s = await GetSmartSwitchStateAsync(id);
                    if (s == desired) { tcs.TrySetResult(true); break; }
                    await Task.Delay(250, cts.Token);
                }
            }
            catch { /* ignore */ }
            finally
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(false);
            }
        });

        var ok = await tcs.Task;
        DeviceStateEvent -= Handler;
        return ok;
    }

}
