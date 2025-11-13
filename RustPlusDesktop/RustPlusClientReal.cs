// Services/RustPlusClientReal.cs
using RustPlusApi;                 // NuGet: HandyS11.RustPlusApi
using RustPlusApi.Data.Events;
using RustPlusDesk.Models;
using RustPlusDesk.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using StorageSnap = RustPlusDesk.Models.StorageSnapshot;
using StorageItemVM = RustPlusDesk.Models.StorageItemVM;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text; // <— hinzufügen
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
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

    public void EnsureEventsHooked() => HookEventsIfNeeded();
    private readonly Dictionary<uint, RustPlusDesk.Models.StorageSnapshot> _storageCache
    = new Dictionary<uint, RustPlusDesk.Models.StorageSnapshot>();
    public bool TryGetCachedStorage(uint id, out RustPlusDesk.Models.StorageSnapshot snap)
     => _storageCache.TryGetValue(id, out snap);
    public event Action<uint, StorageSnap>? StorageSnapshotReceived;


    private void CacheStorage(uint id, StorageSnapshot? snap)
    {
        if (snap == null) return;
        _storageCache[id] = snap;
    }

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


    public async Task<bool> PromoteToLeaderAsync(ulong steamId, CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        var asm = typeof(RustPlus).Assembly;
        var reqType = asm.GetTypes().FirstOrDefault(t => t.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
        if (reqType is null) return false;

        var req = Activator.CreateInstance(reqType)!;

        var promoteProp = reqType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => {
                var n = p.Name.ToLowerInvariant();
                return n.Contains("promote") && n.Contains("leader");
            });
        if (promoteProp is null) return false;

        var body = Activator.CreateInstance(promoteProp.PropertyType)!;
        var idP = body.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(pp => {
                var n = pp.Name.ToLowerInvariant();
                return n.Contains("steam") || n.Contains("player") || n.Contains("member");
            });
        if (idP is null) return false;

        try
        {
            if (idP.PropertyType == typeof(ulong)) idP.SetValue(body, steamId);
            else if (idP.PropertyType == typeof(long)) idP.SetValue(body, unchecked((long)steamId));
            else if (idP.PropertyType == typeof(string)) idP.SetValue(body, steamId.ToString());
            else idP.SetValue(body, Convert.ChangeType(steamId, idP.PropertyType));
        }
        catch { return false; }

        promoteProp.SetValue(req, body);

        var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
        if (send is null) return false;

        var taskObj = send.Invoke(_api, new object[] { req });
        if (taskObj is Task t) await t.ConfigureAwait(false);
        return true;
    }

    public async Task<bool> KickTeamMemberAsync(ulong steamId, CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        var asm = typeof(RustPlus).Assembly;
        var reqType = asm.GetTypes().FirstOrDefault(t => t.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
        if (reqType is null) return false;

        var req = Activator.CreateInstance(reqType)!;

        var kickProp = reqType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => {
                var n = p.Name.ToLowerInvariant();
                return (n.Contains("kick") || n.Contains("remove")) && (n.Contains("team") || n.Contains("member"));
            });
        if (kickProp is null) return false;

        var body = Activator.CreateInstance(kickProp.PropertyType)!;
        var idP = body.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(pp => {
                var n = pp.Name.ToLowerInvariant();
                return n.Contains("steam") || n.Contains("player") || n.Contains("member");
            });
        if (idP is null) return false;

        try
        {
            if (idP.PropertyType == typeof(ulong)) idP.SetValue(body, steamId);
            else if (idP.PropertyType == typeof(long)) idP.SetValue(body, unchecked((long)steamId));
            else if (idP.PropertyType == typeof(string)) idP.SetValue(body, steamId.ToString());
            else idP.SetValue(body, Convert.ChangeType(steamId, idP.PropertyType));
        }
        catch { return false; }

        kickProp.SetValue(req, body);

        var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
        if (send is null) return false;

        var taskObj = send.Invoke(_api, new object[] { req });
        if (taskObj is Task t) await t.ConfigureAwait(false);
        return true;
    }


    private static uint GetEntityId(object e) => ReadProp<uint>(e, "EntityId", "Id");
    private static bool GetIsActive(object e) => ReadProp<bool>(e, "IsActive", "Value", "On");
    private static int GetCapacity(object e) => ReadProp<int>(e, "Capacity");
    private static int GetItemsCount(object e) => ReadProp<int>(e, "ItemsCount", "Items", "ItemCount");

    // ---------- DUMP MAP INFO BUTTON -------------- //
    public sealed record DynMarker2(
     uint Id,
     int RawType,
     string TypeName,
     double X,
     double Y,
     string? Label,
     string? PlayerName,
     ulong SteamId,
     bool HasOrders,
     string DebugLine
 );

    public async Task DumpMapMarkersDeepAsync(int maxPerList = 80, CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        void L(string s) => _log?.Invoke("[dump] " + s);

        // kleine Hilfen
        static object? P(object? o, string n) => o?.GetType().GetProperty(n,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(o);

        static IEnumerable<object> AsEnum(object? o)
            => (o is System.Collections.IEnumerable en && o is not string)
               ? en.Cast<object?>().Where(x => x != null)!
               : Array.Empty<object>();

        // Reccy-Dumper (Properties + kindliche IEnumerables, begrenzt)
        void DumpObject(object o, int depth, HashSet<object> seen)
        {
            if (!seen.Add(o)) return;
            var t = o.GetType();
            string pad = new string(' ', depth * 2);
            L(pad + "⟶ " + t.Name);

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                object? v = null;
                try { v = p.GetValue(o); } catch { }
                if (v == null) { L(pad + $"  {p.Name}=<null>"); continue; }

                if (v is string s)
                {
                    L(pad + $"  {p.Name}=\"{s}\"");
                }
                else if (v.GetType().IsPrimitive || v is decimal || v is DateTime)
                {
                    L(pad + $"  {p.Name}={v}");
                }
                else if (v is System.Collections.IEnumerable en && v is not string)
                {
                    // Kinderlisten andeuten und ein paar Elemente tief dumpen
                    int count = 0;
                    var listHead = new List<object>();
                    foreach (var it in en) { if (it == null) continue; listHead.Add(it); if (++count >= 5) break; }
                    L(pad + $"  {p.Name}=[IEnumerable] head={listHead.Count}{(count >= 5 ? "+" : "")}");
                    foreach (var child in listHead) DumpObject(child, depth + 1, seen);
                }
                else
                {
                    // verschachteltes Objekt
                    L(pad + $"  {p.Name}:");
                    DumpObject(v, depth + 1, seen);
                }
            }
        }

        try
        {
            // Roh-Request bauen (SendRequestAsync(AppRequest{GetMapMarkers}))
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
            var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));
            if (reqType == null || emptyType == null) { L("proto types not found"); return; }

            var req = Activator.CreateInstance(reqType)!;
            reqType.GetProperty("GetMapMarkers",
                BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public)
                   ?.SetValue(req, Activator.CreateInstance(emptyType)!);

            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
            if (send == null) { L("SendRequestAsync not found"); return; }

            var call = send.Invoke(_api, new object[] { req });
            object? resp = call;
            if (call is Task t) { await t.ConfigureAwait(false); resp = t.GetType().GetProperty("Result")?.GetValue(t); }

            var r = P(resp, "Response") ?? resp;
            var mm = P(r, "MapMarkers") ?? r;

            var buckets = new (string name, object? list)[]
            {
            ("Markers",            P(mm,"Markers") ?? P(mm,"Marker")),
            ("VendingMachines",    P(mm,"VendingMachines") ?? P(mm,"Vending")),
            ("Crates",             P(mm,"Crates")),
            ("HackableCrates",     P(mm,"HackableCrates")),
            ("LockedCrates",       P(mm,"LockedCrates"))
            };

            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

            foreach (var (name, listObj) in buckets)
            {
                var items = AsEnum(listObj).Take(maxPerList).ToList();
                L($"== {name}: {items.Count} (showing up to {maxPerList}) ==");
                foreach (var it in items) DumpObject(it, 1, seen);
            }

            // Generischer Scan aller IEnumerable-Props (falls Server exotisch ist)
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Markers","Marker","VendingMachines","Vending","Crates","HackableCrates","LockedCrates" };

            foreach (var p in (mm ?? r)!.GetType().GetProperties())
            {
                if (skip.Contains(p.Name)) continue;
                var v = p.GetValue(mm ?? r);
                var items = AsEnum(v).Take(maxPerList).ToList();
                if (items.Count > 0)
                {
                    L($"== {p.Name}: {items.Count} (head) ==");
                    foreach (var it in items) DumpObject(it, 1, seen);
                }
            }
        }
        catch (Exception ex)
        {
            L("deep-dump error: " + ex.Message);
        }
    }
    public async Task<List<DynMarker2>> GetDynamicMapMarkersAsync2(CancellationToken ct = default)
    {
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        void L(string s) => _log?.Invoke("[dyn2] " + s);

        // --- Reflection-Shortcuts (eindeutige Namen, damit nix kollidiert) ---
        static object? P(object? o, string name)
            => o?.GetType().GetProperty(name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase)
              ?.GetValue(o);

        static string? S(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = P(o, n);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        static int I(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = P(o, n);
                if (v is int i) return i;
                if (v is uint u) return unchecked((int)u);
                if (v is long l) return unchecked((int)l);
                if (v is short sh) return sh;
                if (v is byte b) return b;
                if (v != null && v.GetType().IsEnum) return Convert.ToInt32(v);
                if (int.TryParse(v?.ToString(), out var ii)) return ii;
            }
            return 0;
        }

        static uint UI(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = P(o, n);
                if (v is uint u) return u;
                if (v is int i && i >= 0) return (uint)i;
                if (v is long l && l >= 0) return (uint)l;
                if (uint.TryParse(v?.ToString(), out var uu)) return uu;
            }
            return 0u;
        }

        static ulong UL(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = P(o, n);
                if (v is ulong u) return u;
                if (v is long l && l >= 0) return (ulong)l;
                if (v is uint ui) return ui;
                if (ulong.TryParse(v?.ToString(), out var uu)) return uu;
            }
            return 0UL;
        }

        static double D(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = P(o, n);
                if (v is double dd) return dd;
                if (v is float f) return f;
                if (v is int i) return i;
                if (v is long l) return l;
                if (double.TryParse(v?.ToString(), out var parsed)) return parsed;
            }
            return 0.0;
        }

        static bool TryXY(object it, out double x, out double y)
        {
            var pos = P(it, "Position") ?? P(it, "Pos");
            x = D(pos ?? it, "X", "x", "Lon", "Longitude");
            y = D(pos ?? it, "Y", "y", "Lat", "Latitude");
            if (x == 0 && y == 0)
            {
                foreach (var pr in it.GetType().GetProperties())
                {
                    var v = pr.GetValue(it);
                    if (v == null || v is string) continue;
                    var px = v.GetType().GetProperty("X");
                    var py = v.GetType().GetProperty("Y");
                    if (px != null && py != null)
                    {
                        x = D(v, "X");
                        y = D(v, "Y");
                        break;
                    }
                }
            }
            return !(double.IsNaN(x) || double.IsNaN(y));
        }

        static int CountEnum(object? o)
        {
            if (o is System.Collections.IEnumerable en && o is not string)
            {
                int c = 0;
                foreach (var _ in en) { c++; if (c > 999) break; }
                return c;
            }
            return 0;
        }

        static bool HasOrdersOn(object it)
        {
            var so = P(it, "SellOrders") ?? P(it, "Orders") ?? P(it, "Items");
            if (CountEnum(so) > 0) return true;

            var vend = P(it, "Vending") ?? P(it, "Sales") ?? P(it, "Shop");
            var so2 = P(vend, "SellOrders") ?? P(vend, "Orders") ?? P(vend, "Items");
            return CountEnum(so2) > 0;
        }

        static string Trim(string? s, int max = 80)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().Replace("\r", " ").Replace("\n", " ");
            return (s.Length > max) ? s.Substring(0, max) + "…" : s;
        }

        static string DumpMarkerOneLine(object it)
        {
            var rawType = I(it, "Type", "MarkerType", "TypeId", "TypeID", "type");
            var id = UI(it, "Id", "ID", "EntityId", "Identifier", "Uid", "UID", "MarkerId");
            var label = S(it, "Name", "Label", "Alias", "Token", "Note");
            var pname = S(it, "PlayerName", "DisplayName", "UserName", "SteamName");
            var steamId = UL(it, "SteamId", "SteamID", "Steamid", "PlayerId", "UserId", "UserID");
            TryXY(it, out var x, out var y);

            // ein paar typische Zusatzfelder:
            var radius = D(it, "Radius");
            var rotation = D(it, "Rotation");
            var alpha = D(it, "Alpha", "Opacity");
            var c1 = P(it, "Colour1") ?? P(it, "Color1");
            var c2 = P(it, "Colour2") ?? P(it, "Color2");
            string C(object? c) => (c == null) ? "-" :
                $"({D(c, "X"):0.##},{D(c, "Y"):0.##},{D(c, "Z"):0.##},{D(c, "W"):0.##})";

            var ordersCnt = 0;
            var so = P(it, "SellOrders") ?? P(it, "Orders") ?? P(it, "Items");
            ordersCnt += CountEnum(so);
            var vend = P(it, "Vending") ?? P(it, "Sales") ?? P(it, "Shop");
            var so2 = P(vend, "SellOrders") ?? P(vend, "Orders") ?? P(vend, "Items");
            ordersCnt += CountEnum(so2);

            var tn = it.GetType().Name;

            return $"id={id} type={rawType}({tn}) pos=({x:0},{y:0}) " +
                   $"label='{Trim(label, 60)}' pname='{Trim(pname, 60)}' steam={steamId} " +
                   $"radius={radius:0.##} rot={rotation:0.##} alpha={alpha:0.##} c1={C(c1)} c2={C(c2)} " +
                   $"orders={ordersCnt}";
        }
        // -----------------------------------------------------------------------

        var resultList = new List<DynMarker2>();

        try
        {
            // Roh-PATH (SendRequestAsync(AppRequest{GetMapMarkers})) → enum-sicher
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
            var emptyTyp = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));
            if (reqType == null || emptyTyp == null) return resultList;

            var req = Activator.CreateInstance(reqType)!;
            reqType.GetProperty("GetMapMarkers",
                    System.Reflection.BindingFlags.IgnoreCase |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public)
                   ?.SetValue(req, Activator.CreateInstance(emptyTyp)!);

            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
            if (send == null) return resultList;

            var call = send.Invoke(_api, new object[] { req });
            object? resp = call;
            if (call is Task t)
            {
                await t.ConfigureAwait(false);
                resp = t.GetType().GetProperty("Result")?.GetValue(t);
            }

            var r = P(resp, "Response") ?? resp;
            var mm = P(r, "MapMarkers") ?? r;

            // Hauptquelle
            var markers = P(mm, "Markers") ?? P(mm, "Marker");

            var pool = new List<object>();
            void AddEnum(object? maybe)
            {
                if (maybe is System.Collections.IEnumerable en && maybe is not string)
                    foreach (var it in en) if (it != null) pool.Add(it);
            }

            AddEnum(markers);
            // probiere auch Crate-Container, falls vorhanden:
            AddEnum(P(mm, "Crates"));
            AddEnum(P(mm, "HackableCrates"));
            AddEnum(P(mm, "LockedCrates"));

            // Fallback: alle IEnumerable-Properties durchsuchen (alles aufs Tablett!)
            foreach (var pr in (mm ?? r)!.GetType().GetProperties())
            {
                var v = pr.GetValue(mm ?? r);
                AddEnum(v);
            }

            // Sammle alles, logge kompakt
            int idx = 0;
            foreach (var it in pool)
            {
                idx++;
                // „One-liner“-Dump
                var line = DumpMarkerOneLine(it);
                L($"raw[{idx}]: {line}");

                // in Record überführen (damit du optional auch UI draus machen kannst)
                var rawType = I(it, "Type", "MarkerType", "TypeId", "TypeID", "type");
                var typeNm = it.GetType().Name;
                var id = UI(it, "Id", "ID", "EntityId", "Identifier", "Uid", "UID", "MarkerId");
                var label = S(it, "Name", "Label", "Alias", "Token", "Note");
                var pname = S(it, "PlayerName", "DisplayName", "UserName", "SteamName");
                var steamId = UL(it, "SteamId", "SteamID", "Steamid", "PlayerId", "UserId", "UserID");

                TryXY(it, out var x, out var y);
                var hasOrders = HasOrdersOn(it);

                resultList.Add(new DynMarker2(
                    id, rawType, typeNm, x, y, label, pname, steamId, hasOrders, line
                ));
            }

            L($"raw-total={resultList.Count}");
        }
        catch (Exception ex)
        {
            L("error: " + ex.Message);
        }
        try
        {
            var byType = resultList
                .GroupBy(m => (m.RawType, m.TypeName))
                .OrderBy(g => g.Key.RawType)
                .Select(g => $"{g.Key.RawType}({g.Key.TypeName})×{g.Count()}");
            L("raw-type-dist: " + string.Join(", ", byType));

            // explizit nach crate/hack/lock in Label/TypeName suchen
            bool Token(string? s) =>
                !string.IsNullOrWhiteSpace(s) &&
                (s.IndexOf("crate", StringComparison.OrdinalIgnoreCase) >= 0
              || s.IndexOf("hack", StringComparison.OrdinalIgnoreCase) >= 0
              || s.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0);

            var crateLike = resultList.Where(m => Token(m.Label) || Token(m.TypeName)).ToList();
            L($"raw-crateLike: {crateLike.Count}");
            foreach (var c in crateLike.Take(6))
                L("raw-crateLike sample: " + c.DebugLine);
        }
        catch { /* tolerate */ }
        return resultList;
    }


    // CAMERA FEEDS

    // === CAMERA FALLBACK (Node snapshot, robust) ===============================


    // Hilfsreflektion (lokal)
    static object? RProp(object? o, string name) =>
        o?.GetType().GetProperty(name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.IgnoreCase
        )?.GetValue(o);

    // Liefert Host/Port/PlayerId/Token – zuerst aus deinen Feldern, dann via Reflection vom _api
    private void GetConnForCamera(out string host, out int port, out string playerId, out string token)
    {
        host = _host ?? "";
        port = _port;
        playerId = (_steamId != 0 ? _steamId.ToString() : "");
        token = (_playerToken != 0 ? _playerToken.ToString() : "");

        // falls etwas fehlt, versuche vom _api zu lesen
        if (_api != null && (string.IsNullOrWhiteSpace(host) || port <= 0 ||
                             string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(token)))
        {
            host = string.IsNullOrWhiteSpace(host)
                     ? (RProp(_api, "Address") as string) ?? (RProp(_api, "Host") as string) ?? (RProp(_api, "Ip") as string) ?? host
                     : host;

            if (port <= 0)
            {
                try { port = Convert.ToInt32(RProp(_api, "Port") ?? 0); } catch { /* ignore */ }
            }

            playerId = string.IsNullOrWhiteSpace(playerId)
                     ? (RProp(_api, "PlayerId") as string) ?? (RProp(_api, "SteamId") as string) ?? playerId
                     : playerId;

            token = string.IsNullOrWhiteSpace(token)
                     ? (RProp(_api, "PlayerToken") as string) ?? (RProp(_api, "Token") as string) ?? token
                     : token;
        }
    }

    private static void PatchNearPlaneIfNeeded(string rustplusPkgDir, Action<string>? log = null)
    {
        try
        {
            // Wir suchen die generierten Protobuf-Dateien nach der "nearPlane"-Pflichtprüfung ab
            var files = Directory.GetFiles(rustplusPkgDir, "*.js", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                var text = File.ReadAllText(f);
                if (!text.Contains("missing required 'nearPlane'")) continue;
                if (text.Contains("/*RPD_PATCH_NEARPLANE*/"))
                {
                    log?.Invoke("[cam-node] nearPlane patch already present: " + Path.GetFileName(f));
                    return;
                }

                // harte, aber robuste Ersetzung: die Throw-Stelle durch Default ersetzen
                var patched = text.Replace(
                    "throw util.ProtocolError(\"missing required 'nearPlane'\",{instance:m})",
                    "/*RPD_PATCH_NEARPLANE*/ if(m.nearPlane==null) m.nearPlane=0"
                );

                if (!ReferenceEquals(patched, text))
                {
                    File.WriteAllText(f, patched);
                    log?.Invoke("[cam-node] patched nearPlane in: " + f);
                    return;
                }
            }

           // log?.Invoke("[cam-node] nearPlane check not found; no patch applied");
        }
        catch (Exception ex)
        {
            log?.Invoke("[cam-node] patch failed: " + ex.Message);
        }
    }

    public event Action<string /*cameraId*/, double /*vFovDeg*/, int /*w*/, int /*h*/, List<CameraEntity> /*(x,y,z,name)*/>? CameraEntities;

    private string? _currentCamId;  // setzen, wenn wir subscriben
    private (int w, int h, double vfov) _lastCamInfo = (160, 90, 65);


    public async Task<CameraFrame?> GetCameraFrameViaNodeAsync(string cameraId, int timeoutMs = 5000, CancellationToken ct = default)
    {



        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        void L(string s) => _log?.Invoke("[cam-node] " + s);

        // --- statt der Reflection auf _api aus Helper ziehen:
        GetConnForCamera(out var host, out var port, out var playerId, out var token);

        // kurzes Debug ohne Geheimnisse:
      //  _log?.Invoke($"[cam] conn host={(string.IsNullOrWhiteSpace(host) ? "-" : host)} port={(port > 0 ? port : 0)} pidLen={playerId?.Length ?? 0} tokLen={token?.Length ?? 0}");

        if (string.IsNullOrWhiteSpace(host) || port <= 0 ||
            string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Connection data for camera snapshot incomplete.");
        }

        // ---- Node + rustplus.js Verzeichnis finden ----
        string? nodeExe = FindBundledNode();
        if (nodeExe == null) throw new FileNotFoundException("Node Runtime not found (runtime/node-win-x64/node.exe).");

        string? pkgRoot = FindRustplusJsPackageRoot(); // Ordner, der *node_modules* enthält
        if (pkgRoot == null) throw new DirectoryNotFoundException("rustplus.js Package not found (runtime/rustplus-cli/node_modules).");



        // One-shot JS: lädt Paket *aus dem Working-Dir* (pkgRoot) und schreibt Base64 auf STDOUT
        var js = """
// CLI-Args
const host   = process.argv[2];
const port   = parseInt(process.argv[3], 10);
const pid    = process.argv[4];
const tok    = process.argv[5];
const cam    = process.argv[6];
const tmo    = parseInt(process.argv[7], 10) || 5000;
const pkgDir = process.argv[8]; // ...\node_modules\@liamcottle\rustplus.js
// console.error("using pkg dir: " + pkgDir);

function reqFrom(base, id){
  return require(require.resolve(id, { paths: [base] }));
}

// === wir hooken *genau* die protobufjs-Instanz, die rustplus.js nutzen wird
function hookProtobuf(pb){
  if (!pb) return;
  const RELAX_ALL = true;              // <- global alles ‚required‘ -> ‚optional‘
  const RELAX = new Set([              // falls du einschränken willst: Felder hier benennen
    "nearPlane","controlFlags","sampleOffset","sampleCount","horizontalFov","verticalFov","fov"
  ]);

  const shouldRelax = (fname) => RELAX_ALL || RELAX.has(fname);

  function relaxFieldsObj(obj){
    if (!obj || typeof obj !== "object") return;
    if (obj.fields && typeof obj.fields === "object"){
      for (const [fname, f] of Object.entries(obj.fields)){
        if (!f) continue;
        if ((f.rule === "required" || f.required === true) && shouldRelax(fname)){
          f.rule = "optional";
          f.required = false;
        }
      }
    }
    for (const k of Object.keys(obj)) relaxFieldsObj(obj[k]);
  }

  // Hook: Root.fromJSON
  if (pb.Root && typeof pb.Root.fromJSON === "function"){
    const orig = pb.Root.fromJSON;
    pb.Root.fromJSON = function(json, root){
      try{ relaxFieldsObj(json); }catch{}
      return orig.call(this, json, root);
    };
  }
  // Hook: Type.fromJSON
  if (pb.Type && typeof pb.Type.fromJSON === "function"){
    const orig = pb.Type.fromJSON;
    pb.Type.fromJSON = function(name, json){
      try{
        if (json && json.fields){
          for (const [fname, f] of Object.entries(json.fields)){
            if ((f.rule === "required" || f.required === true) && shouldRelax(fname)){
              f.rule = "optional";
              f.required = false;
            }
          }
        }
      }catch{}
      return orig.call(this, name, json);
    };
  }
  // Hook: Type.prototype.add (für dynamisch erzeugte Felder)
  if (pb.Type && pb.Type.prototype){
    const origAdd = pb.Type.prototype.add;
    pb.Type.prototype.add = function(field){
      try{
        if (field && (field.rule === "required" || field.required === true) && shouldRelax(field.name)){
          field.rule = "optional";
          field.required = false;
        }
      }catch{}
      return origAdd.call(this, field);
    };
  }

  // Safety-Net: bereits geladene Typen nachträglich entspannen
  function relaxAlreadyBuilt(){
    try{
      const roots = pb.roots ? Object.values(pb.roots) : [];
      for (const root of roots){
        if (!root) continue;
        try { root.resolveAll(); } catch {}
        (function walk(ns){
          if (!ns) return;
          if (ns.fields && typeof ns.fields === "object"){
            for (const [fname, f] of Object.entries(ns.fields)){
              if ((f.rule === "required" || f.required === true) && shouldRelax(fname)){
                f.rule = "optional";
                f.required = false;
              }
            }
          }
          if (ns.nested){
            for (const v of Object.values(ns.nested)) walk(v);
          }
        })(root);
      }
    }catch{}
  }

  // console.error(`protobufjs hook ready (global relax=${RELAX_ALL})`);
  return { relaxAlreadyBuilt };
}

// 1) protobufjs laden & hooken
let pb = null;
try { pb = reqFrom(pkgDir, "protobufjs"); }
catch { try { pb = reqFrom(pkgDir, "protobufjs/minimal"); } catch {} }
const hook = hookProtobuf(pb);

// 2) jetzt rustplus.js laden (damit unser Hook greift)
const RustPlus = reqFrom(pkgDir, "@liamcottle/rustplus.js");

// 3) Safety-Net: falls Types schon gebaut wurden, nochmals entschärfen
try { hook && hook.relaxAlreadyBuilt && hook.relaxAlreadyBuilt(); } catch {}

// 4) Kamera benutzen
const rp = new RustPlus(host, port, pid, tok);
let timer = null;

rp.on("connected", async () => {
 // console.error("CONNECTED");
  try {
    const c = rp.getCamera(cam);

    // ---- Message-Handler (beide Ebenen) ----
    const onMsg = (m) => {
      try {
        // Tolerant über mögliche Pfade gehen
        const resp = (m && (m.response || m.appMessage || m.data)) || m || {};
        const broadcast = resp.broadcast || resp.appBroadcast || {};
        const rays = broadcast.cameraRays || broadcast.appCameraRays || resp.cameraRays || null;

        if (rays && Array.isArray(rays.entities)) {
          const ents = rays.entities.map(e => ({
            id:  e.entityId || 0,
            type: e.type || 0,
            name: e.name || "",
            x: (e.position && e.position.x) || 0,
            y: (e.position && e.position.y) || 0,
            z: (e.position && e.position.z) || 0,
            sid:    (e.steamId || e.playerId || 0),
            sidStr: (e.steamId || e.playerId) ? String(e.steamId || e.playerId) : ""
          }));
          const payload = { cam, ents, fov: rays.verticalFov || rays.fov || 65 };
          process.stdout.write("ENTS:" + Buffer.from(JSON.stringify(payload), "utf8").toString("base64") + "\n");
          // console.error(`RAYS n=${ents.length}`);
        }

        const info = resp.cameraInfo || resp.appCameraInfo || null;
        if (info && (info.width || info.height)) {
          const w = info.width || 0, h = info.height || 0;
          process.stdout.write("INFO:" + Buffer.from(JSON.stringify({ cam, w, h }), "utf8").toString("base64") + "\n");
        }
      } catch { /* schlucken */ }
    };

    rp.on("message", onMsg);
    c .on("message", onMsg);

    // ---- Frame-Listener ----
    c.on("render", async (frame) => {
     // console.error("RENDER");
      try {
        process.stdout.write("FRAME:" + Buffer.from(frame).toString("base64") + "\n");
      } finally {
        // Gib Broadcasts Zeit anzukommen (fix 400ms)
        setTimeout(async () => {
          try { await c.unsubscribe(); } catch {}
          try { rp.disconnect(); } catch {}
        }, 400);
      }
    });

    await c.subscribe();
  //  console.error("SUBSCRIBED");

    // Sicherheitsnetz
    timer = setTimeout(() => { console.error("TIMEOUT"); try { rp.disconnect(); } catch {} }, Math.max(1000, tmo));
  } catch (e) {
    console.error("ERR:" + (e && e.message ? e.message : String(e)));
    try { rp.disconnect(); } catch {}
  }
});

rp.on("error", (e) => {
  try {
    const msg = (e && (e.message || e.code)) ? `${e.message||e.code}` : JSON.stringify(e);
    console.error("ERR:" + msg);
  } catch { console.error("ERR:unknown"); }
});
rp.connect();
""";

        var rustplusPkgDir = Path.Combine(pkgRoot, "node_modules", "@liamcottle", "rustplus.js");
        PatchNearPlaneIfNeeded(rustplusPkgDir, _log);
        if (!Directory.Exists(rustplusPkgDir))
            throw new DirectoryNotFoundException("[cam-node] rustplus.js package not found at: " + rustplusPkgDir);
        // _log?.Invoke("[cam-node] using pkg dir: " + rustplusPkgDir);

        var tempDir = Path.Combine(Path.GetTempPath(), "RustPlusDesk");
        Directory.CreateDirectory(tempDir);
        var jsFile = Path.Combine(tempDir, "camera_once.js");
        File.WriteAllText(jsFile, js);

        var psi = new ProcessStartInfo(nodeExe,
    $"\"{jsFile}\" {host} {port} {playerId} {token} {cameraId} {timeoutMs} \"{rustplusPkgDir}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = pkgRoot
        };

        // optional: NODE_PATH setzen (hilft, falls WorkingDirectory doch mal nicht greift)
       // psi.Environment["NODE_PATH"] = pkgRoot;

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var p = new System.Diagnostics.Process { StartInfo = psi };
        var lastEnts = new List<CameraEntity>();
        int widthHint = 0, heightHint = 0;

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            // FRAME:<b64>
            if (e.Data.StartsWith("FRAME:"))
            {
                try
                {
                    var b64 = e.Data.Substring("FRAME:".Length);
                    tcs.TrySetResult(Convert.FromBase64String(b64));
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
                return;
            }

            // ENTS:<b64 json>
            if (e.Data.StartsWith("ENTS:"))
            {
                try
                {
                    var b = Convert.FromBase64String(e.Data.Substring(5));
                    var json = System.Text.Encoding.UTF8.GetString(b);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    lastEnts.Clear();

                    double vFov = 65.0;
                    if (doc.RootElement.TryGetProperty("fov", out var vf) && vf.ValueKind == System.Text.Json.JsonValueKind.Number)
                        vFov = vf.GetDouble();

                    if (doc.RootElement.TryGetProperty("ents", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            double x = it.TryGetProperty("x", out var vx) ? vx.GetDouble() : 0;
                            double y = it.TryGetProperty("y", out var vy) ? vy.GetDouble() : 0;
                            double z = it.TryGetProperty("z", out var vz) ? vz.GetDouble() : 0;
                            int entityId = it.TryGetProperty("id", out var vi) ? (vi.ValueKind == System.Text.Json.JsonValueKind.Number ? vi.GetInt32() : 0) : 0;
                            int type = it.TryGetProperty("type", out var vt) ? (vt.ValueKind == System.Text.Json.JsonValueKind.Number ? vt.GetInt32() : 0) : 0;
                            ulong sid = 0;

                            // bevorzugt: String-sichere ID (falls im JS ausgegeben)
                            if (it.TryGetProperty("sidStr", out var vss) && vss.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                _ = ulong.TryParse(vss.GetString(), out sid);
                            }
                            else if (it.TryGetProperty("sid", out var vs) && vs.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                try { sid = vs.GetUInt64(); } catch { sid = 0; } // falls out of range / falsches Format
                            }

                            string name = it.TryGetProperty("name", out var vn) ? (vn.GetString() ?? "") : "";

                            lastEnts.Add(new CameraEntity(x, y, z, name, entityId, type, sid));
                            
                        }
                    }

                    // kleines Histogramm zur Typ-Erkundung
                    var byType = lastEnts.GroupBy(en => en.Type)
                                         .Select(g => $"{g.Key}={g.Count()}");
                    //_log?.Invoke($"[cam-node] ents={lastEnts.Count} vfov={vFov} wHint={widthHint} hHint={heightHint} types=[{string.Join(", ", byType)}]");

                    // -> UI

                    CameraEntities?.Invoke(cameraId, vFov, widthHint, heightHint, lastEnts.ToList());
                }
                catch { /* ignore */ }
                return;
            }

            // INFO:<b64 json>
            if (e.Data.StartsWith("INFO:"))
            {
                try
                {
                    var b = Convert.FromBase64String(e.Data.Substring(5));
                    var json = System.Text.Encoding.UTF8.GetString(b);
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    widthHint = doc.RootElement.TryGetProperty("w", out var w) ? w.GetInt32() : 0;
                    heightHint = doc.RootElement.TryGetProperty("h", out var h) ? h.GetInt32() : 0;
                }
                catch { /* ignore */ }
                return;
            }

            // sonstiges Log wie gehabt
            L(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            L(e.Data); // alles loggen
            if (e.Data.StartsWith("ERR:"))
                tcs.TrySetException(new Exception(e.Data));
            if (e.Data.IndexOf("Cannot find module", StringComparison.OrdinalIgnoreCase) >= 0)
                tcs.TrySetException(new FileNotFoundException(e.Data));
            if (e.Data.StartsWith("TIMEOUT"))
                tcs.TrySetCanceled();
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs + 2000);

        try
        {
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs + 1500, cts.Token));
            if (completed != tcs.Task) return null;

            try
            {
                var bytes = await tcs.Task.ConfigureAwait(false); // may throw
                return new CameraFrame(bytes, null, widthHint, heightHint, lastEnts);
            }
            catch (OperationCanceledException)
            {
                _log?.Invoke("[cam-node] cancelled");
                return null;
            }
            catch (Exception ex)
            {
                _log?.Invoke("[cam-node] exception: " + ex.Message);
                return null;
            }
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
        }

        // --- lokale Finder ---
        static string? FindBundledNode()
        {
            var p1 = Path.Combine(AppContext.BaseDirectory, "runtime", "node-win-x64", "node.exe");
            if (File.Exists(p1)) return p1;
            var p2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "runtime", "node-win-x64", "node.exe"));
            return File.Exists(p2) ? p2 : null;
        }

        static string? FindRustplusJsPackageRoot()
        {
            // wir brauchen den Ordner, der die *node_modules* enthält
            var candidates = new[]
            {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustPlusDesk","runtime","rustplus-cli"), // Release (ZIP entpackt)
            Path.Combine(AppContext.BaseDirectory, "runtime","rustplus-cli"), // neben EXE
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..","..","..","runtime","rustplus-cli")) // Dev
        };
            foreach (var root in candidates)
            {
                if (Directory.Exists(Path.Combine(root, "node_modules", "@liamcottle", "rustplus.js")))
                    return root;
            }
            return null;
        }
    }

    // --- minimaler Node-Finder (kopie deiner Pairing-Helfer, gekürzt) ---
    private static string? FindBundledNode()
    {
        var p1 = Path.Combine(AppContext.BaseDirectory, "runtime", "node-win-x64", "node.exe");
        if (File.Exists(p1)) return p1;
        var p2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "runtime", "node-win-x64", "node.exe"));
        return File.Exists(p2) ? p2 : null;
    }



    private static Delegate BuildWildcardHandler(EventInfo ev, Action<object, object> onInvoke)
    {
        var handlerType = ev.EventHandlerType!;
        var invoke = handlerType.GetMethod("Invoke")!;
        var ps = invoke.GetParameters();
        if (ps.Length != 2) throw new NotSupportedException("Only EventHandler-like delegates supported");

        var senderParam = System.Linq.Expressions.Expression.Parameter(ps[0].ParameterType, "s");
        var argsParam = System.Linq.Expressions.Expression.Parameter(ps[1].ParameterType, "e");

        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(onInvoke),
            typeof(Action<object, object>).GetMethod(nameof(Action<object, object>.Invoke))!,
            System.Linq.Expressions.Expression.Convert(senderParam, typeof(object)),
            System.Linq.Expressions.Expression.Convert(argsParam, typeof(object))
        );

        var lambda = System.Linq.Expressions.Expression.Lambda(handlerType, call, senderParam, argsParam);
        return lambda.Compile();
    }

    private static EventInfo? FindMessageEvent(object api)
    {
        // grab an event that *sounds* like "message"
        foreach (var ev in api.GetType().GetEvents(BindingFlags.Instance | BindingFlags.Public))
        {
            var n = ev.Name.ToLowerInvariant();
            if (n.Contains("message") || n.Contains("received") || n.Contains("app"))
                return ev;
        }
        return null;
    }

    private static object? P(object? o, string name) =>
        o?.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(o);

    private static byte[]? ReadBytesFlexible(object? obj)
    {
        if (obj is null) return null;
        if (obj is byte[] b) return b;

        foreach (var prop in obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var n = prop.Name.ToLowerInvariant();
            if (!(n.Contains("png") || n.Contains("jpg") || n.Contains("jpeg") ||
                  n.Contains("image") || n.Contains("bytes") || n.Contains("data") || n.Contains("frame")))
                continue;

            var v = prop.GetValue(obj);
            if (v is byte[] bb) return bb;
            if (v is string s)
            {
                try { return Convert.FromBase64String(s); } catch { }
            }
            if (v != null)
            {
                // one more level deep
                if (v is byte[] b2) return b2;
                if (v is string s2) { try { return Convert.FromBase64String(s2); } catch { } }
            }
        }
        return null;
    }

    private static (PropertyInfo reqProp, PropertyInfo idProp)? FindCameraReq(object appRequestInstance, string kind /*"subscribe"/"unsubscribe"*/)
    {
        var reqType = appRequestInstance.GetType();
        // look for AppRequest.{CameraSubscribe}/{CameraUnsubscribe} or similar
        foreach (var p in reqType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var n = p.Name.ToLowerInvariant();
            if (!(n.Contains("camera") || n.Contains("cctv"))) continue;
            if (!(n.Contains("sub") == (kind == "subscribe") || n.Contains("unsub") == (kind == "unsubscribe")))
                continue;

            var child = Activator.CreateInstance(p.PropertyType)!;
            // find string id/name prop on the child
            var idProp = child.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(pp =>
                    pp.PropertyType == typeof(string) &&
                    (pp.Name.Contains("identifier", StringComparison.OrdinalIgnoreCase) ||
                     pp.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                     pp.Name.Contains("name", StringComparison.OrdinalIgnoreCase) ||
                     pp.Name.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
                     pp.Name.Contains("cctv", StringComparison.OrdinalIgnoreCase)));

            if (idProp != null) return (p, idProp);
        }
        return null;
    }



    public event Action<string, byte[]>? CameraFrame; // (cameraId, jpeg/png bytes)

    private bool _camPushHooked;
    private readonly Dictionary<string, TaskCompletionSource<byte[]>> _camAwaitOnce
        = new(StringComparer.OrdinalIgnoreCase);

   

    private void EnsureCameraPushHooked()
    {
        if (_camPushHooked || _api is null) return;
        _camPushHooked = true;

        // Wir hängen uns "breit" an Events vom _api. Der Handler prüft dann auf CameraFrames.
        foreach (var ev in _api.GetType().GetEvents(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            // Wir akzeptieren EventHandler oder EventHandler<T>
            var ehType = ev.EventHandlerType;
            if (ehType is null) continue;
            var invoke = ehType.GetMethod("Invoke");
            if (invoke is null) continue;
            var pars = invoke.GetParameters();
            if (pars.Length != 2) continue; // (sender, args)

            // dynamisch: (object? s, object e) => OnAnyApiEvent(e);
            var method = GetType().GetMethod(nameof(OnAnyApiEvent),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var d = Delegate.CreateDelegate(ehType, this, method!);
            ev.AddEventHandler(_api, d);
        }
    }

    private static object? RProp2(object? o, string name) =>
    o?.GetType().GetProperty(name,
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.IgnoreCase)?.GetValue(o);

    private static string? RStr(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = RProp2(o, n);
            if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static byte[]? RBytes(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = RProp2(o, n);
            if (v is byte[] b && b.Length > 0) return b;
            if (v is string s && s.Length > 0)
            {
                try { return Convert.FromBase64String(s); } catch { }
            }
        }
        return null;
    }

    // universeller Event-Slot: wir versuchen, CameraFrames aus beliebigen EventArgs zu lesen
    private int _anyEvCount;
    private void OnAnyApiEvent(object? sender, object e)
    {
        try
        {
            // typische Kette: e -> Message/Response/AppMessage -> Camera/CCTV -> bytes
            var msg = RProp2(e, "Message") ?? RProp2(e, "AppMessage") ?? e;
            var resp = RProp2(msg, "Response") ?? RProp2(msg, "Data") ?? msg;

            // Mögliche Containernamen für Camera-Frames
            var cam = RProp2(resp, "CameraFrame") ??
                      RProp2(resp, "CCTVFrame") ??
                      RProp2(resp, "CCTV") ??
                      RProp2(resp, "Camera");

            // Kamera-Info (Breite/Höhe/FOV) mitnehmen, wenn vorhanden
            var info = RProp2(resp, "CameraInfo") ?? RProp2(resp, "AppCameraInfo");
            if (info != null)
            {
                int w = (int)(RProp2(info, "Width") ?? 160);
                int h = (int)(RProp2(info, "Height") ?? 90);
                double vf = 0;
                double.TryParse(RProp2(info, "VerticalFov")?.ToString(), out vf);
                if (vf <= 0) vf = _lastCamInfo.vfov;
                _lastCamInfo = (w, h, vf);
            }

            // Rays/Entities
            var rays = RProp2(resp, "CameraRays") ?? RProp2(resp, "AppCameraRays") ?? RProp2(resp, "Rays");
            if (rays != null)
            {
                double vf = _lastCamInfo.vfov;
                double.TryParse(RProp2(rays, "VerticalFov")?.ToString(), out vf);
                if (vf <= 0) vf = _lastCamInfo.vfov;

                var entsNode = RProp2(rays, "Entities") ?? RProp2(rays, "Entity");
                var list = new List<CameraEntity>();
                if (entsNode is System.Collections.IEnumerable en)
                {
                    foreach (var it in en)
                    {
                        var name = RStr(it, "Name", "Label", "UserName", "DisplayName") ?? "";
                        var pos = RProp2(it, "Position") ?? it;
                        double.TryParse(RProp2(pos, "X")?.ToString(), out var ex);
                        double.TryParse(RProp2(pos, "Y")?.ToString(), out var ey);
                        double.TryParse(RProp2(pos, "Z")?.ToString(), out var ez);
                        // optional: id/type mitgeben, falls vorhanden
                        int id2 = 0, type = 0;
                        if (int.TryParse(RProp2(en, "entityId")?.ToString(), out var eid)) id2 = eid;
                        if (int.TryParse(RProp2(en, "type")?.ToString(), out var et)) type = et;
                    }
                }
                CameraEntities?.Invoke(_currentCamId ?? "", vf, _lastCamInfo.w, _lastCamInfo.h, list);
            }

            if (cam == null) return;

            var id = RStr(cam, "CameraId", "Identifier", "Id", "Name") ?? "";
            var bytes = RBytes(cam, "JpegImage", "PngImage", "Frame", "Image", "Bytes", "Data");
            if (bytes is null || bytes.Length == 0) return;

            // Event an UI weiterreichen
            CameraFrame?.Invoke(id, bytes);

            // Warten auf "einmaliges" Snapshot erfüllen?
            if (!string.IsNullOrEmpty(id) && _camAwaitOnce.TryGetValue(id, out var tcs) && !tcs.Task.IsCompleted)
            {
                tcs.TrySetResult(bytes);
            }
        }
        catch
        {
            // still – wir sniffen hier generisch
        }
    }

    private (object? req, object? idProp) MakeCamSubscribeRequest(string cameraId)
    {
        var asm = typeof(RustPlus).Assembly;
        var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
        if (reqType == null) return (null, null);

        // Kandidaten: CameraSubscribe / CCTVSubscribe / SubscribeCamera / GetCCTVStatic / GetCameraFrame
        // Wir hier bauen explizit ein Subscribe – Frames kommen per Push.
        var subProp = reqType.GetProperty("CameraSubscribe",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
            ?? reqType.GetProperty("CCTVSubscribe",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
            ?? reqType.GetProperty("SubscribeCamera",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);

        if (subProp == null) return (null, null);

        var subType = subProp.PropertyType;
        var subObj = Activator.CreateInstance(subType)!;

        // Id-Feld finden
        var idP = subType.GetProperty("CameraId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
                 ?? subType.GetProperty("Identifier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
                 ?? subType.GetProperty("Id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
                 ?? subType.GetProperty("Name", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        if (idP == null) return (null, null);

        idP.SetValue(subObj, cameraId);

        var req = Activator.CreateInstance(reqType)!;
        subProp.SetValue(req, subObj);
        return (req, idP);
    }

    private (object? req, System.Reflection.PropertyInfo? idProp) MakeCamUnsubscribeRequest(string cameraId)
    {
        var asm = typeof(RustPlus).Assembly;
        var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
        if (reqType == null) return (null, null);

        var unsubProp = reqType.GetProperty("CameraUnsubscribe",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
            ?? reqType.GetProperty("CCTVUnsubscribe",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
            ?? reqType.GetProperty("UnsubscribeCamera",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        if (unsubProp == null) return (null, null);

        var subType = unsubProp.PropertyType;
        var subObj = Activator.CreateInstance(subType)!;

        var idP = subType.GetProperty("CameraId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
                 ?? subType.GetProperty("Identifier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
                 ?? subType.GetProperty("Id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)
                 ?? subType.GetProperty("Name", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        if (idP == null) return (null, null);

        idP.SetValue(subObj, cameraId);

        var req = Activator.CreateInstance(reqType)!;
        unsubProp.SetValue(req, subObj);
        return (req, idP);
    }

    /// <summary>Startet Streaming (Subscribe) und liefert optional direkt das erste Frame (Snapshot) innerhalb Timeout.</summary>
    public async Task<byte[]?> StartCameraStreamAsync(string cameraId, int firstFrameTimeoutMs = 2000, CancellationToken ct = default)
    {
        _currentCamId = cameraId;
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

        EnsureCameraPushHooked();

        // First-frame TCS einrichten
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _camAwaitOnce[cameraId] = tcs;

        var (req, _) = MakeCamSubscribeRequest(cameraId);
        if (req == null)
        {
            _log?.Invoke("[cam] no subscribe property found on AppRequest");
            _camAwaitOnce.Remove(cameraId);
            return null;
        }

        try
        {
            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { req.GetType() });
            if (send == null) { _log?.Invoke("[cam] SendRequestAsync not found"); _camAwaitOnce.Remove(cameraId); return null; }

            var taskObj = send.Invoke(_api, new object[] { req });
            if (taskObj is Task t) await t.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Invoke("[cam] subscribe error: " + ex.Message);
            _camAwaitOnce.Remove(cameraId);
            return null;
        }

        // Auf erstes Frame warten (optional)
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(firstFrameTimeoutMs);
            var frame = await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            return frame;
        }
        catch
        {
            _log?.Invoke("[cam] subscribed, but no pushed frame arrived in time");
            _camAwaitOnce.Remove(cameraId);
            return null; // UI kann trotzdem weiter auf CameraFrame-Event hören
        }
        finally
        {
            _camAwaitOnce.Remove(cameraId);
        }
    }

    public async Task StopCameraStreamAsync(string cameraId)
    {
        if (_api is null) return;
        var (req, _) = MakeCamUnsubscribeRequest(cameraId);
        if (req == null) return;
        try
        {
            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { req.GetType() });
            if (send == null) return;
            var taskObj = send.Invoke(_api, new object[] { req });
            if (taskObj is Task t) await t.ConfigureAwait(false);
        }
        catch { /* tolerant */ }
    }

    private readonly HashSet<string> _camBusy = new(StringComparer.OrdinalIgnoreCase);
    private int _camThumbIndex = 0; // rotiert über die Tiles, damit alle mal dran kommen

    public async Task<CameraFrame?> GetCameraFrameAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("identifier required");
        if (_api is null) throw new InvalidOperationException("Nicht verbunden.");
        void L(string s) => _log?.Invoke("[cam] " + s);

        // 1) build and send AppRequest.CameraSubscribe
        var appReqType = typeof(RustPlus).Assembly
            .GetTypes().FirstOrDefault(t => t.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
        if (appReqType is null) { L("AppRequest type not found"); return null; }

        var req = Activator.CreateInstance(appReqType)!;
        var sub = FindCameraReq(req, "subscribe");
        if (sub == null) { L("no camera subscribe property on AppRequest"); return null; }

        var child = Activator.CreateInstance(sub.Value.reqProp.PropertyType)!;
        sub.Value.idProp.SetValue(child, identifier);
        sub.Value.reqProp.SetValue(req, child);

        var send = _api.GetType().GetMethod("SendRequestAsync", new[] { appReqType });
        if (send == null) { L("SendRequestAsync not found"); return null; }

        // 2) attach a one-shot tap to the websocket "message" event
        var ev = FindMessageEvent(_api);
        if (ev == null) { L("no message event on api"); return null; }

        var tcs = new TaskCompletionSource<CameraFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        void OnAny(object _, object argsObj)
        {
            try
            {
                // try common shapes:
                // argsObj.Message.Response.CameraFrame.*  OR  argsObj.Response.CameraFrame.*  OR  argsObj.CameraFrame
                object? root = argsObj;

                var msg = P(root, "Message") ?? root;
                var resp = P(msg, "Response") ?? msg;

                object? cameraNode =
                    P(resp, "CameraFrame") ??
                    P(resp, "Camera") ??
                    P(resp, "Cctv") ??
                    P(resp, "Frame") ??
                    resp;

                var bytes = ReadBytesFlexible(cameraNode) ?? ReadBytesFlexible(P(cameraNode, "Data"));
                if (bytes == null) return;

                // optional dimensions
                int w = 0, h = 0;
                int TryInt(object? o, string n)
                {
                    var v = P(o, n);
                    if (v is int i) return i;
                    if (int.TryParse(v?.ToString(), out var ii)) return ii;
                    return 0;
                }
                w = TryInt(cameraNode, "Width"); if (w == 0) w = TryInt(P(cameraNode, "Data"), "Width");
                h = TryInt(cameraNode, "Height"); if (h == 0) h = TryInt(P(cameraNode, "Data"), "Height");

                tcs.TrySetResult(new CameraFrame(bytes, null, w, h, new List<CameraEntity>()));
            }
            catch { /* ignore and keep waiting */ }
        }

        var handler = BuildWildcardHandler(ev, OnAny);
        ev.AddEventHandler(_api, handler);

        try
        {
            // send subscribe (push frames will follow)
            var call = send.Invoke(_api, new object[] { req });
            if (call is Task t) await t.ConfigureAwait(false);

            using (cts.Token.Register(() => tcs.TrySetResult(null)))
            {
                var frame = await tcs.Task.ConfigureAwait(false);
                if (frame == null) _log?.Invoke("[cam] subscribed, but no pushed frame arrived in time");
                return frame;
            }
        }
        finally
        {
            // detach handler
            try { ev.RemoveEventHandler(_api, handler); } catch { }

            // best-effort unsubscribe
            try
            {
                var un = FindCameraReq(Activator.CreateInstance(appReqType)!, "unsubscribe");
                if (un != null)
                {
                    var ch = Activator.CreateInstance(un.Value.reqProp.PropertyType)!;
                    un.Value.idProp.SetValue(ch, identifier);
                    un.Value.reqProp.SetValue(req, ch);
                    var ucall = send.Invoke(_api, new object[] { req });
                    if (ucall is Task ut) await ut.ConfigureAwait(false);
                }
            }
            catch { /* ignore */ }
        }
    }

    private static void DumpTree(object? o, Action<string> log, string prefix = "[cam-dump] ", int depth = 0, int maxDepth = 5)
    {
        if (o == null || depth > maxDepth) return;
        var t = o.GetType();
        string indent = new string(' ', depth * 2);
        log($"{prefix}{indent}{t.Name}");

        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            object? v = null;
            try { v = p.GetValue(o); } catch { }
            var vt = v?.GetType();

            if (v == null)
            {
                log($"{prefix}{indent}- {p.Name} = <null>");
            }
            else if (v is string s)
            {
                log($"{prefix}{indent}- {p.Name} (string) = \"{(s.Length > 80 ? s[..80] + "…" : s)}\"");
            }
            else if (v is System.Collections.IEnumerable en && v is not byte[] && v is not string)
            {
                int i = 0;
                log($"{prefix}{indent}- {p.Name} = [IEnumerable] {vt?.Name}");
                foreach (var it in en)
                {
                    if (i++ > 20) { log($"{prefix}{indent}  …"); break; }
                    DumpTree(it, log, prefix, depth + 2, maxDepth);
                }
            }
            else if (v is byte[] bb)
            {
                log($"{prefix}{indent}- {p.Name} (byte[]) len={bb.Length}");
            }
            else if (vt != null && vt.FullName!.StartsWith("System."))
            {
                log($"{prefix}{indent}- {p.Name} ({vt.Name}) = {v}");
            }
            else
            {
                log($"{prefix}{indent}- {p.Name} -> {vt?.Name}");
                DumpTree(v, log, prefix, depth + 1, maxDepth);
            }
        }
    }

    // ----- END CAMERA STUFF -----


    public void DebugDumpAppRequestShape()
    {
        var asm = typeof(RustPlus).Assembly;
        var reqType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
        if (reqType == null) { _log?.Invoke("[cam] AppRequest type not found"); return; }
        _log?.Invoke("[cam] AppRequest properties:");
        foreach (var p in reqType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            _log?.Invoke("  - " + p.Name);
    }

    // ---------- END DUMP MAP INFO BUTTON -------------- //

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
    private readonly object _hookLock = new();
    private void HookEventsIfNeeded()
    {
        if (_api is null) return;
        lock (_hookLock)
        {
            if (_eventsHooked) return;
            
        }

        // --- ab hier: Events/Sniffer VERDRAHTEN ---
        _log?.Invoke("[stor/sniff] events: " + string.Join(", ", _api.GetType().GetEvents().Select(e => e.Name)));

        AttachSniffer("OnStorageMonitorTriggered");
        AttachSniffer("OnEntityChanged");
        AttachSniffer("OnEntityInfo");
        AttachSniffer("OnResponse");
        AttachSniffer("OnProtobufMessage");

        foreach (var ev in _api.GetType().GetEvents()
                 .Where(e => e.Name.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0
                          || e.Name.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0
                          || e.Name.IndexOf("Entity", StringComparison.OrdinalIgnoreCase) >= 0
                          || e.Name.IndexOf("App", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            AttachSniffer(ev.Name);
        }

        _api.OnSmartSwitchTriggered += (_, sw) =>
        {
            var id = GetEntityId(sw);
            var on = GetIsActive(sw);
            DeviceStateEvent?.Invoke(id, on, "SmartSwitch");
            _log?.Invoke($"[Gerät] {id} → {(on ? "AN" : "AUS")}");
        };

        _api.OnStorageMonitorTriggered += (_, st) =>
        {
            try
            {
                uint entityId = 0;
                try
                {
                    var idObj = TryGetProp(st!, "Id") ?? TryGetProp(st!, "EntityId");
                    entityId = Convert.ToUInt32(idObj ?? 0);
                }
                catch { }

                var root = UnpackAnyRecursive(st!) ?? (object)st!;
                var payload = TryGetProp(root, "Payload") ?? root;
                var info = TryGetProp(payload, "StorageMonitor", "storageMonitor", "Storage", "Container") ?? payload;
                info = UnpackAnyRecursive(info!) ?? info;

                int? upkeep = null;
                if (!TryReadUpkeepSeconds(info!, out var secs))
                {
                    // Fallback via ProtectionExpiry
                    var secs2 = SecondsUntil(info!, "ProtectionExpiry", "Expiry", "ProtectedUntil", "ProtectedExpiry");
                    if (secs2.HasValue) upkeep = secs2.Value;
                }
                else upkeep = secs;

                var items = FindItemsList(info!);

                var snap = new StorageSnap { UpkeepSeconds = upkeep, IsToolCupboard = (upkeep ?? 0) > 0 };
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        if (it == null) continue;
                        int id = ReadIntFlexible(it, "ItemId", "ItemID", "Id") ?? 0;
                        int amt = ReadIntFlexible(it, "Amount", "Quantity", "Count", "Stack") ?? 0;
                        int? mx = ReadIntFlexible(it, "MaxStack", "MaxStackSize", "StackSize");
                        string? sn = ReadStringFlexible(it, "ShortName", "ItemShortName", "Short", "Name");
                        snap.Items.Add(new StorageItemVM { ItemId = id, ShortName = sn, Amount = amt, MaxStack = mx });
                    }
                }

                _storageCache[entityId] = snap;
                StorageSnapshotReceived?.Invoke(entityId, snap);
                _log?.Invoke($"[stor/event] {entityId} items={snap.Items.Count} upkeep={(snap.UpkeepSeconds?.ToString() ?? "null")}");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[stor/event] EX: {ex.Message}");
            }
        };

        _api.RequestSent += (_, reqObj) =>
        {
            try
            {
                var root = UnpackAnyRecursive(reqObj!) ?? (object)reqObj!;
                var envelope =
                       TryGetProp(root, "Request")
                    ?? TryGetProp(root, "AppRequest")
                    ?? TryGetProp(root, "Message")
                    ?? root;

                // Seq nullable lesen
                var seqN = TryReadIntN(envelope, "Seq", "Sequence", "SequenceId");
                if (seqN is null) return;

                // EntityId versuchen (direkt am Envelope)
                uint entityId = TryReadUIntN(envelope, "EntityId", "Id") ?? 0u;

                // Wenn nicht vorhanden: in Sub-Operationen nachsehen
                if (entityId == 0u)
                {
                    foreach (var opName in new[] { "GetEntityInfo", "GetStorageMonitor", "GetStorage", "GetContainer", "GetEntityStorage" })
                    {
                        var op = TryGetProp(envelope, opName);
                        if (op is null) continue;

                        // zuerst am Sub-Objekt prüfen
                        entityId = TryReadUIntN(op, "EntityId", "Id") ?? 0u;
                        if (entityId != 0u) break;

                        // manche Builds halten EntityId trotzdem am Envelope
                        entityId = TryReadUIntN(envelope, "EntityId", "Id") ?? 0u;
                        if (entityId != 0u) break;
                    }
                }

                if (entityId != 0u)
                {
                    _seqToEntity[seqN.Value] = entityId;
                    // _log?.Invoke($"[stor/seq] req seq={seqN.Value} → entity={entityId}");
                }
            }
            catch { /* tolerant */ }
        };


        // ---- GENERISCHER RESPONSE-HANDLER: EntityInfo → Storage/Container herausparsen ----
        _api.ResponseReceived += (_, respObj) =>
        {
            try
            {
                // 1) Envelope tolerant + Any auspacken
                var root = UnpackAnyRecursive(respObj!) ?? (object)respObj!;
                var envelope =
                       TryGetProp(root, "Response")
                    ?? TryGetProp(root, "AppResponse")
                    ?? TryGetProp(root, "AppMessage")
                    ?? TryGetProp(root, "Message")
                    ?? root;

                // 2) Seq → EntityId per Map
                uint entityId = 0u;
                var seqN = TryReadIntN(envelope, "Seq", "Sequence", "SequenceId");
                if (seqN is not null && _seqToEntity.TryGetValue(seqN.Value, out var mapped))
                    entityId = mapped;

                // 3) EntityInfo / Info (Hüllenschicht) holen
                var entityInfoOrInfo =
                       TryGetProp(envelope, "EntityInfo")
                    ?? TryGetProp(envelope, "Entity")
                    ?? TryGetProp(envelope, "EntityInfoResponse")
                    ?? TryGetProp(envelope, "Info");
                if (entityInfoOrInfo is null) return;

                // Payload/Info auspacken
                var payload = TryGetProp(entityInfoOrInfo, "Payload");
                payload = UnpackAnyRecursive(payload) ?? payload;

                // Type check: nur Storage/Container/Cupboard
                var typeStr =
                       TryReadStringN(entityInfoOrInfo, "Type", "EntityType")
                    ?? TryReadStringN(payload, "Type", "EntityType");
                if (typeStr != null)
                {
                    var ts = typeStr.ToLowerInvariant();
                    if (!(ts.Contains("storage") || ts.Contains("container") || ts.Contains("cupboard")))
                        return; // nicht relevant
                }

                // EntityId ggf. aus Tiefen lesen (wenn Map nicht gegriffen hat)
                if (entityId == 0u)
                {
                    entityId = TryReadUIntN(entityInfoOrInfo, "EntityId", "Id") ?? 0u;
                    if (entityId == 0u) entityId = TryReadUIntN(payload, "EntityId", "Id") ?? 0u;
                }

                // 4) Info → Storage-Knoten tolerant
                var info = TryGetProp(payload, "Info") ?? payload ?? entityInfoOrInfo;
                info = UnpackAnyRecursive(info) ?? info;

                var stor =
                       TryGetProp(info, "StorageMonitor", "storageMonitor", "Storage", "Container", "Box", "ToolCupboard", "Cupboard")
                    ?? info;
                stor = UnpackAnyRecursive(stor) ?? stor;
                if (stor is Type) return;

                // 5) Upkeep & Items extrahieren
                int? upkeep = null;
                if (TryReadUpkeepSeconds(stor, out var secs)) upkeep = secs;

                var seqItems = FindItemsList(stor) as IEnumerable;
                if (seqItems is null && upkeep is null) return;

                // Für Diagnose kurz loggen
               // _log?.Invoke($"[stor/resp] hit entity={(entityId == 0 ? -1 : (int)entityId)}");

                var snap = new StorageSnap
                {
                    UpkeepSeconds = upkeep,
                    IsToolCupboard = (upkeep ?? 0) > 0
                };

                if (seqItems is not null)
                {
                    foreach (var it in seqItems)
                    {
                        if (it is null) continue;
                        int id = TryReadIntN(it, "ItemId", "ItemID", "Id") ?? 0;
                        int amt = TryReadIntN(it, "Amount", "Quantity", "Count", "Stack") ?? 0;
                        int? mx = TryReadIntN(it, "MaxStack", "MaxStackSize", "StackSize");
                        string? sn = TryReadStringN(it, "ShortName", "ItemShortName", "Short", "Name");
                        snap.Items.Add(new StorageItemVM { ItemId = id, ShortName = sn, Amount = amt, MaxStack = mx });
                       // _log?.Invoke($"[stor/item] #{entityId} [{sn}] id={id} sn='{sn ?? "-"}' amt={amt} max={mx.ToString() ?? "-"}");
                    }
                }

                if (entityId != 0u)
                {
                    _storageCache[entityId] = snap;
                    StorageSnapshotReceived?.Invoke(entityId, snap);
                   // _log?.Invoke($"[stor/resp] {entityId} items={snap.Items.Count} upkeep={(snap.UpkeepSeconds?.ToString() ?? "null")}");
                }
                else
                {
                  //  _log?.Invoke($"[stor/resp] (no entity id) items={snap.Items.Count} upkeep={(snap.UpkeepSeconds?.ToString() ?? "null")}");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[stor/resp] EX: {ex.Message}");
            }
        };
        // --- Diagnose: einmal Event-Namen loggen + gezielt anbinden ---
        LogAllApiEventsOnce();
        // gezielt die Transport-/Entity-Events anhängen (falls vorhanden)
        AttachSniffer("ResponseReceived");
        AttachSniffer("MessageReceived");
        AttachSniffer("RequestSent");
        AttachSniffer("SendingRequest");
        AttachSniffer("NotificationReceived");

        // zusätzlich die in deinem Build vorhandenen Entity/Storage-Events
        AttachSniffer("OnStorageMonitorTriggered");
        AttachSniffer("OnEntityInfo");
        AttachSniffer("OnEntityChanged");

        
        lock (_hookLock) _eventsHooked = true;
    }

    private bool HasAnyProp(object? o, params string[] names)
    {
        if (o is null) return false;
        foreach (var n in names)
            if (TryGetProp(o, n) != null) return true;
        return false;
    }

    private bool LooksLikeStorageEntityInfo(object respObj, out object? envelope, out object? entityInfoOrInfo, out uint entityId)
    {
        envelope = null; entityInfoOrInfo = null; entityId = 0;

        var root = UnpackAnyRecursive(respObj) ?? (object)respObj;

        envelope =
            TryGetProp(root, "Response") ??
            TryGetProp(root, "AppResponse") ??
            TryGetProp(root, "AppMessage") ??
            TryGetProp(root, "Message") ??
            root;

        // Muss mindestens eine EntityInfo-/Info-Hülle enthalten
        entityInfoOrInfo =
            TryGetProp(envelope, "EntityInfo") ??
            TryGetProp(envelope, "Entity") ??
            TryGetProp(envelope, "EntityInfoResponse") ??
            TryGetProp(envelope, "Info");

        if (entityInfoOrInfo is null) return false;

        // Info-Knoten bestimmen
        var info = TryGetProp(entityInfoOrInfo, "Info") ?? entityInfoOrInfo;
        info = UnpackAnyRecursive(info) ?? info;

        // Muss einen Storage-ähnlichen Node enthalten
        if (!HasAnyProp(info, "StorageMonitor", "storageMonitor", "Storage", "Container", "Box", "Cupboard"))
            return false;

        // EntityId (optional, aber nützlich)
        try
        {
            var idObj =
                TryGetProp(entityInfoOrInfo, "EntityId") ??
                TryGetProp(entityInfoOrInfo, "Id") ??
                TryGetProp(envelope, "EntityId") ??
                TryGetProp(envelope, "Id");
            if (idObj != null) entityId = Convert.ToUInt32(idObj);
        }
        catch { /* tolerant */ }

        return true;
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

            if (resObj is null) { 
                //_log("[chat-history] mapped=0 afterFilter=0 since=" + (sinceUtc?.ToString("u") ?? "null")); 
                return result; }

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
        public ulong LeaderSteamId { get; set; }
        public List<Member> Members { get; } = new();

        public sealed class Member
        {
            public ulong SteamId { get; set; }
            public string? Name { get; set; }

            public bool Online { get; set; }   // neu
            public bool Dead { get; set; }   // neu
            public double? X { get; set; }   // neu
            public double? Y { get; set; }   // neu
        }
    }

    public async Task<TeamInfo?> GetTeamInfoAsync(CancellationToken ct = default)
{
    if (_api is null) throw new InvalidOperationException("Nicht verbunden.");

    static object? P(object? o, string name) =>
        o?.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(o);

    static ulong AsULong(object? v)
    {
        if (v is ulong u) return u;
        if (v is long  l && l >= 0) return (ulong)l;
        if (v is uint  ui) return ui;
        if (v is string s && ulong.TryParse(s, out var p)) return p;
        try { return Convert.ToUInt64(v); } catch { return 0UL; }
    }

    static bool AsBool(object? v)
    {
        if (v is bool b) return b;
        if (v is string s && bool.TryParse(s, out var p)) return p;
        try { return Convert.ToInt32(v) != 0; } catch { return false; }
    }

    static double? AsDouble(object? v)
    {
        if (v is null) return null;
        if (v is double d) return d;
        if (v is float  f) return f;
        if (double.TryParse(v.ToString(), out var dd)) return dd;
        try { return Convert.ToDouble(v); } catch { return null; }
    }
        static int AsInt(object? v) { try { return Convert.ToInt32(v); } catch { return 0; } }   // ← NEU
        try
    {
        var asm = typeof(RustPlus).Assembly;
        var reqType   = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
        var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name.Equals("AppEmpty",   StringComparison.OrdinalIgnoreCase));
        if (reqType == null || emptyType == null) return null;

        var req = Activator.CreateInstance(reqType)!;
        reqType.GetProperty("GetTeamInfo",
                BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public)
            ?.SetValue(req, Activator.CreateInstance(emptyType)!);

        var send   = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
        if (send == null) return null;

        var taskObj = send.Invoke(_api, new object[] { req });
        object? resp = taskObj;
        if (taskObj is Task tsk)
        {
            await tsk.ConfigureAwait(false);
            resp = tsk.GetType().GetProperty("Result")?.GetValue(tsk);
        }

        var r  = P(resp, "Response") ?? resp;
        var ti = P(r, "TeamInfo") ?? r;

        // Leader aus verschiedenen Feldern versuchen
        ulong leaderId =
              AsULong(P(ti, "LeaderSteamId"))
            | AsULong(P(ti, "LeaderId"))
            | AsULong(P(ti, "TeamLeaderSteamId"))
            | AsULong(P(ti, "Leader"));

        // Members-Container finden
        object? members =
              P(ti, "Members")
           ?? P(ti, "TeamMembers")
           ?? P(ti, "TeamInfo"); // manche Builds packen die Liste hier rein

        var list = new TeamInfo { LeaderSteamId = leaderId };

        if (members is System.Collections.IEnumerable en)
        {
            foreach (var m in en)
            {
                var sid = AsULong(P(m, "SteamId") ?? P(m, "UserId") ?? P(m, "PlayerId") ?? P(m, "Id"));
                if (sid == 0) continue;

                string? name =
                     (P(m, "Name") ?? P(m, "DisplayName") ?? P(m, "PlayerName") ?? P(m, "Username"))?.ToString();

                // online/dead
                bool online = AsBool(P(m, "Online") ?? P(m, "IsOnline"));
                    // a) direkte „Dead/IsDead“
                    bool dead = AsBool(P(m, "Dead") ?? P(m, "IsDead") ?? P(m, "dead"));

                    // b) Alive/IsAlive kehrt Dead ggf. um
                    if (!dead)
                    {
                        object? aliveObj = P(m, "Alive") ?? P(m, "IsAlive");
                        if (aliveObj != null)
                            dead = !AsBool(aliveObj);
                    }

                    // c) LifeState Heuristik (häufig: 0=Alive, 1=Wounded, 2=Dead)
                    int lifeState = AsInt(P(m, "LifeState") ?? P(m, "Lifestate"));
                    if (!dead && (lifeState == 2 || lifeState == 1)) dead = true;

                    // d) Wounded / IsWounded -> „als tot“ behandeln
                    if (!dead && AsBool(P(m, "Wounded") ?? P(m, "IsWounded"))) dead = true;

                    // Position: direkt X/Y oder in Position/Pos
                    var pos = P(m, "Position") ?? P(m, "Pos");
                double? x = AsDouble(P(m, "X") ?? P(pos, "X"));
                double? y = AsDouble(P(m, "Y") ?? P(pos, "Y"));

                list.Members.Add(new TeamInfo.Member
                {
                    SteamId = sid,
                    Name = name,
                    Online = online,
                    Dead = dead,
                    X = x,
                    Y = y
                });
            }
        }

        return list;
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
           // L("pathA ignored: " + ex.Message);
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
        var typeStr = ReadProp<object>(ent, "Type", "EntityType", "EntType")?.ToString();
        string? kind = null;
        if (!string.IsNullOrWhiteSpace(typeStr))
        {
            if (typeStr.Contains("Alarm", StringComparison.OrdinalIgnoreCase)) kind = "SmartAlarm";
            else if (typeStr.Contains("Switch", StringComparison.OrdinalIgnoreCase)) kind = "SmartSwitch";
            else if (typeStr.Contains("Storage", StringComparison.OrdinalIgnoreCase)) kind = "StorageMonitor"; // NEU
        }

        // Name-Heuristik, falls Type leer/„server“
        if (string.IsNullOrWhiteSpace(kind))
        {
            var name = ReadProp<string>(ent, "Name", "DisplayName", "EntityName") ?? "";
            if (name.IndexOf("alarm", StringComparison.OrdinalIgnoreCase) >= 0) kind = "SmartAlarm";
            else if (name.IndexOf("stor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("mon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("cup", StringComparison.OrdinalIgnoreCase) >= 0) kind = "StorageMonitor";
            else kind = "SmartSwitch";
        }

        // StorageMonitor hat KEINEN Schalterzustand
        if (string.Equals(kind, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
            return (kind, null);

        // SmartAlarm: nur echte Power-Flags zulassen
        if (string.Equals(kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
        {
            string[] powerNames = { "IsPowered", "HasPower", "PowerOn", "Powered", "HasElectricity", "IsOn", "On" };
            var v = ReadBoolFlexible(ent, powerNames);
            if (v != null) return (kind, v);
            foreach (var p in ent.GetType().GetProperties())
            {
                if (!p.PropertyType.IsValueType && p.PropertyType != typeof(string))
                {
                    var sub = p.GetValue(ent);
                    var sv = ReadBoolFlexible(sub, powerNames);
                    if (sv != null) return (kind, sv);
                }
            }
            return (kind, null);
        }

        // Standard (SmartSwitch)
        var preferred = new[] { "IsOn", "On", "Value", "Active", "Enabled", "IsActive", "PowerOn" };
        var direct = ReadBoolFlexible(ent, preferred);
        if (direct != null) return (kind, direct);
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

    public async Task<(ulong leaderId, List<(ulong sid, string name, bool? online, double? x, double? y)> members)?> GetTeamInfoRawAsync()
    {
        if (_api is null) return null;

        static object? P(object? o, string n) =>
            o?.GetType().GetProperty(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase)?.GetValue(o);

        static T Get<T>(object? o, T def, params string[] names)
        {
            foreach (var n in names)
            {
                var v = P(o, n);
                if (v is null) continue;
                try
                {
                    if (v is T t) return t;
                    if (typeof(T) == typeof(string)) return (T)(object)Convert.ToString(v)!;
                    if (typeof(T) == typeof(bool)) return (T)(object)Convert.ToBoolean(v);
                    if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(v);
                    if (typeof(T) == typeof(long)) return (T)(object)Convert.ToInt64(v);
                    if (typeof(T) == typeof(ulong)) return (T)(object)Convert.ToUInt64(v);
                    if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(double?)) return (T)(object)Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
                    if (typeof(T) == typeof(bool?)) return (T)(object)Convert.ToBoolean(v);
                }
                catch { }
            }
            return def;
        }

        try
        {
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(t => t.Name.Equals("AppRequest", StringComparison.OrdinalIgnoreCase));
            var empty = asm.GetTypes().FirstOrDefault(t => t.Name.Equals("AppEmpty", StringComparison.OrdinalIgnoreCase));
            if (reqType is null || empty is null) return null;

            var req = Activator.CreateInstance(reqType)!;

            // Property auf AppRequest suchen, die nach "GetTeamInfo" klingt
            var pGetTeam = reqType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                                  .FirstOrDefault(p => p.Name.Contains("Team", StringComparison.OrdinalIgnoreCase) &&
                                                       (p.Name.Contains("Info", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Get", StringComparison.OrdinalIgnoreCase)));
            if (pGetTeam is null) return null;

            pGetTeam.SetValue(req, Activator.CreateInstance(empty)!);

            var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
            if (send is null) return null;

            var taskObj = send.Invoke(_api, new object[] { req });
            object? resp = taskObj;
            if (taskObj is Task t) { await t.ConfigureAwait(false); resp = t.GetType().GetProperty("Result")?.GetValue(t); }

            var r = P(resp, "Response") ?? resp;
            var ti = P(r, "TeamInfo") ?? P(r, "Team") ?? r;

            // Leader
            ulong leader = Get<ulong>(ti, 0UL, "LeaderSteamId", "LeaderId", "TeamLeaderSteamId", "Leader");

            // Members-Knoten
            var membersNode = P(ti, "Members") ?? P(ti, "TeamMembers") ?? P(ti, "Players") ?? ti;
            var list = new List<(ulong sid, string name, bool? online, double? x, double? y)>();

            if (membersNode is System.Collections.IEnumerable en)
            {
                foreach (var m in en)
                {
                    // SteamId kann string/zahl sein
                    ulong sid = Get<ulong>(m, 0UL, "SteamId", "PlayerId", "Id");
                    if (sid == 0)
                    {
                        var s = Get<string>(m, "", "SteamId", "PlayerId", "Id");
                        if (!string.IsNullOrWhiteSpace(s)) ulong.TryParse(s, out sid);
                    }

                    var name = Get<string>(m, "", "Name", "DisplayName", "Username");
                    bool? on = Get<bool?>(m, null, "IsOnline", "Online", "Alive", "IsAlive");
                    double? x = Get<double?>(m, null, "X", "PosX", "MapX", "PositionX");
                    double? y = Get<double?>(m, null, "Y", "PosY", "MapY", "PositionY");

                    if (sid != 0)
                        list.Add((sid, name ?? "", on, x, y));
                }
            }

            return (leader, list);
        }
        catch
        {
            return null;
        }
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
        HookEventsIfNeeded();

        foreach (var id in entityIds.Distinct())
        {
            await EnsureSubOnceAsync(id);     // sorgt fürs Event
           // await PokeEntityAsync(id, ct);      // triggert erstes Update
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

            // Bonus:Id/PlayerToken am Request setzen (falls vorhanden)
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
        bool sent = false;

        if (await TryToggleExplicitAsync(id, on, ct)) { _log($"[toggle:{id}] path=explicit"); sent = true; }
        else
        {
            _log($"[toggle:{id}] path=explicit ✗");
            var compat = await TrySetEntityValueCompatAsync(id, on);
            if (compat == true) { _log($"[toggle:{id}] path=setEntityValue ✔"); sent = true; }
            else
            {
                if (compat == false) _log($"[toggle:{id}] path=setEntityValue ✗");
                var (ok3, e3) = await TrySendContractsViaCurrentApiAsync(id, on, RequestTimeoutMs);
                if (ok3) { _log($"[toggle:{id}] path=contracts ✔"); sent = true; }
                else
                {
                    _log($"[toggle:{id}] path=contracts ✗ ({e3})");
                    var (okLegacy, e4) = await TrySetViaLegacyWithResultAsync_Timeout(id, on, RequestTimeoutMs);
                    _log(okLegacy ? $"[toggle:{id}] path=legacy ✔" : $"[toggle:{id}] path=legacy ✗ ({e4})");
                    sent = okLegacy;
                }
            }
        }

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
            if (appRequestType == null) return (false, "AppRequest not found");

            var appSetType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppSetEntityValue");
            var appTurnType = asm.GetTypes().FirstOrDefault(t => t.Name == "AppTurnSmartSwitch");

            if (appSetType == null && appTurnType == null)
                return (false, "Neither AppSetEntityValue nor AppTurnSmartSwitch found");

            var req = Activator.CreateInstance(appRequestType)!;
            appRequestType.GetProperty("EntityId")?.SetValue(req, entityId);

            if (appSetType != null)
            {
                var set = Activator.CreateInstance(appSetType)!;
                var pValue = appSetType.GetProperty("Value")    // übliche Shape
                          ?? appSetType.GetProperty("On")
                          ?? appSetType.GetProperty("TurnOn");
                if (pValue == null) return (false, "AppSetEntityValue.Value missing");
                pValue.SetValue(set, on);
                appRequestType.GetProperty("SetEntityValue")?.SetValue(req, set);
            }
            else
            {
                var turn = Activator.CreateInstance(appTurnType!)!;
                var pOn = appTurnType!.GetProperty("TurnOn")
                       ?? appTurnType.GetProperty("On")
                       ?? appTurnType.GetProperty("Value");
                if (pOn == null) return (false, "AppTurnSmartSwitch.TurnOn missing");
                pOn.SetValue(turn, on);
                appRequestType.GetProperty("TurnSmartSwitch")?.SetValue(req, turn);
            }

            var send = _api!.GetType().GetMethod("SendRequestAsync", new[] { appRequestType });
            if (send == null) return (false, "SendRequestAsync not found");

            var taskObj = send.Invoke(_api, new object[] { req });
            if (taskObj is not Task task) return (false, "SendRequestAsync returned no Task");

            var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
            if (done != task) return (false, "timeout");

            var ok = TryGetTaskResultSuccess(task); // wertet IsSuccess/Fehlertext aus, falls vorhanden
            return (ok ?? true, ok is null ? "no success info" : null);
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

        // 1) Fast-Path
        try
        {
            var res = await _api.GetSmartSwitchInfoAsync(entityId).ConfigureAwait(false);
            if (res != null)
            {
                var data = res.GetType().GetProperty("Data")?.GetValue(res) ?? res;
                if (data != null)
                {
                    var dt = data.GetType();
                    var p = dt.GetProperty("IsActive")
                          ?? dt.GetProperty("IsOn")
                          ?? dt.GetProperty("Active")
                          ?? dt.GetProperty("value");
                    if (p?.PropertyType == typeof(bool))
                        return (bool)p.GetValue(data)!;
                }
            }
        }
        catch (NullReferenceException)
        {
            // bekannte NRE aus der Lib -> ruhig auf Fallback gehen
        }
        catch (Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            if (msg.IndexOf("not a SmartSwitch", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            // sonst still auf Fallback
        }

        // 2) Fallback A: GetEntityInfoAsync (wenn vorhanden)
        try
        {
            var t = _api.GetType();
            var m = t.GetMethod("GetEntityInfoAsync", new[] { typeof(uint) })
                  ?? t.GetMethod("GetEntityInfoAsync", new[] { typeof(uint), typeof(CancellationToken) });
            if (m != null)
            {
                object? call = (m.GetParameters().Length == 2)
                    ? m.Invoke(_api, new object[] { entityId, CancellationToken.None })
                    : m.Invoke(_api, new object[] { entityId });

                if (call is Task task)
                {
                    await task.ConfigureAwait(false);
                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                    var data = result?.GetType().GetProperty("Data")?.GetValue(result);
                    if (data != null)
                    {
                        var (kind, on) = DecodeEntityInfo(data);
                        if (string.Equals(kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) && on is bool b) return b;
                    }
                }
            }
        }
        catch { /* weiter zu Fallback B */ }

        // 3) Fallback B: Contracts GetEntityInfo
        try
        {
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(x => x.Name == "AppRequest");
            var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name == "AppEmpty");
            if (reqType != null && emptyType != null)
            {
                var req = Activator.CreateInstance(reqType)!;
                var empty = Activator.CreateInstance(emptyType)!;

                reqType.GetProperty("EntityId")?.SetValue(req, entityId);
                reqType.GetProperty("GetEntityInfo")?.SetValue(req, empty);

                var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
                if (send != null)
                {
                    var call = send.Invoke(_api, new object[] { req });
                    if (call is Task task)
                    {
                        await task.ConfigureAwait(false);
                        var result = task.GetType().GetProperty("Result")?.GetValue(task);
                        var resp = result?.GetType().GetProperty("Response")?.GetValue(result) ?? result;
                        var ent = resp?.GetType().GetProperty("EntityInfo")?.GetValue(resp);
                        if (ent != null)
                        {
                            var (kind, on) = DecodeEntityInfo(ent);
                            if (string.Equals(kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) && on is bool b) return b;
                        }
                    }
                }
            }
        }
        catch { }

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


    // ---- helpers lokal
    static object? GetProp(object? o, params string[] names)
    {
        if (o is null) return null;
        var t = o.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase);
            if (p != null) return p.GetValue(o);
        }
        return null;
    }

    static int? RPReadInt(object? o, params string[] names)
    {
        if (o is null) return null;
        foreach (var n in names)
        {
            var v = GetProp(o, n);
            if (v is null) continue;
            if (v is int i) return i;
            if (v is uint u) return unchecked((int)u);
            if (v is long l) return unchecked((int)l);
            if (int.TryParse(v.ToString(), out var j)) return j;
        }
        return null;
    }

    static string? RPReadStr(object? o, params string[] names)
    {
        if (o is null) return null;
        foreach (var n in names)
        {
            var v = GetProp(o, n);
            if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    public async Task<StorageSnapshot?> GetStorageMonitorAsync(uint entityId, CancellationToken ct = default)
    {
        if (_api is null) return null;

        object? result = null;
        var t = _api.GetType();

        // a) Primär: GetEntityInfoAsync(uint[,CT])
        {
            var m = t.GetMethod("GetEntityInfoAsync", new[] { typeof(uint), typeof(CancellationToken) })
                  ?? t.GetMethod("GetEntityInfoAsync", new[] { typeof(uint) });
            if (m != null)
            {
                object? call = (m.GetParameters().Length == 2)
    ? m.Invoke(_api, new object[] { entityId, ct })
    : m.Invoke(_api, new object[] { entityId });

                if (call is Task task)
                {
                    try
                    {
                        await task.ConfigureAwait(false);
                        if (!TryGetTaskResult(task, out result))
                            _log?.Invoke("[stor/pull] GetEntityInfoAsync returned non-generic Task or no Result");
                    }
                    catch (Exception ex)
                    {
                        LogTaskException("stor/pull:GetEntityInfoAsync", ex);
                        result = null;
                    }
                }
                else
                {
                    _log?.Invoke("[stor/pull] GetEntityInfoAsync invoke returned non-Task");
                }
            }
        }

        if (result is null)
        {
            _log?.Invoke($"[stor/pull] #{entityId} returned null (will rely on ResponseReceived)");
            return null;
        }

        // --- Sichtbar machen, was wir haben (kurz aktivieren) ---
        DumpShapeLog(result, "pull.result");

        var resp = TryGetProp(result, "Response") ?? TryGetProp(result, "AppResponse") ?? result;
        DumpShapeLog(resp, "pull.response");

        var ent = TryGetProp(resp, "EntityInfo") ?? TryGetProp(resp, "Entity") ?? resp;
        DumpShapeLog(ent, "pull.entityInfo");

        // ✳️ AppEntityInfo → Payload (Any) auspacken
        var payload = TryGetProp(ent, "Payload");
        payload = UnpackAnyRecursive(payload) ?? payload;
        DumpShapeLog(payload, "pull.payload");

        // Optional: Info-Feld tolerant
        var info = TryGetProp(payload, "Info") ?? payload ?? ent;
        info = UnpackAnyRecursive(info) ?? info;

        // Storage tolerant finden
        var stor = TryGetProp(info, "StorageMonitor", "storageMonitor", "Storage", "Container", "Box", "ToolCupboard", "Cupboard") ?? info;
        stor = UnpackAnyRecursive(stor) ?? stor;
        DumpShapeLog(stor, "pull.storage");

        if (stor is Type)
        {
            _log?.Invoke("[stor/pull] storage node is Type – aborting this path");
            return null;
        }

        // ---------- Upkeep & Items ----------
        int? upkeep = null;
        if (TryReadUpkeepSeconds(stor, out var seconds)) upkeep = seconds;
        var itemsEnum = FindItemsList(stor) as IEnumerable;

        var snap = new StorageSnapshot
        {
            UpkeepSeconds = upkeep,
            IsToolCupboard = (upkeep ?? 0) > 0
        };

        if (itemsEnum != null)
        {
            foreach (var it in itemsEnum)
            {
                if (it is null) continue;
                var id = ReadIntFlexible(it, "ItemId", "ItemID", "Id") ?? 0;
                var amt = ReadIntFlexible(it, "Amount", "Quantity", "Count", "Stack") ?? 0;
                var mstk = ReadIntFlexible(it, "MaxStack", "MaxStackSize", "StackSize");
                var sn = ReadStringFlexible(it, "ShortName", "ItemShortName", "Short", "Name");
                snap.Items.Add(new StorageItemVM { ItemId = id, ShortName = sn, Amount = amt, MaxStack = mstk });
            }
        }

        // WICHTIG: NICHT mehr 'return null'! Cache + return.
        if (snap.Items.Count == 0 && snap.UpkeepSeconds is null)
            _log?.Invoke($"[stor/pull] #{entityId} had no items/upkeep in immediate response (waiting for event/resp)");

        CacheStorage(entityId, snap);
        return snap;
    }

    // Helpers (nutzen dein bestehendes ReadProp/ReadBoolFlexible-Schema)

    static void DumpShape(object? o, string tag, int depth = 0, int maxDepth = 5)
    {
        if (o == null || depth > maxDepth) return;
        var t = o.GetType();
        Debug.WriteLine($"{new string(' ', depth * 2)}[{tag}] {t.FullName}");
        foreach (var p in t.GetProperties().Take(20))
        {
            try
            {
                var v = p.GetValue(o);
                var vt = v?.GetType().Name ?? "null";
                Debug.WriteLine($"{new string(' ', depth * 2)}  - {p.Name}: {vt}");
                if (v != null && !(v is string) && !p.PropertyType.IsValueType)
                    DumpShape(v, p.Name, depth + 1, maxDepth);
            }
            catch { }
        }
    }

    private static bool TryReadUpkeepSeconds(object? src, out int seconds)
    {
        seconds = 0;
        if (src is null || src is Type) return false;

        // 0) kleine Helper
        static int? IntOf(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = TryGetProp(o!, n);
                if (v == null) continue;
                if (v is int i) return i;
                if (v is long l) return unchecked((int)l);
                if (v is double d) return (int)Math.Round(d);
                if (int.TryParse(v.ToString(), out var ii)) return ii;
                if (double.TryParse(v.ToString(), out var dd)) return (int)Math.Round(dd);
            }
            return null;
        }
        static string? StrOf(object? o, params string[] names)
        {
            foreach (var n in names)
            {
                var v = TryGetProp(o!, n);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            return null;
        }

        // 1) direkte Sekunden
        var num = IntOf(src,
            "UpkeepSeconds", "UpkeepSec", "ProtectionSeconds",
            "SecondsRemaining", "TimeRemainingSeconds", "Seconds",
            "ProtectedSeconds", "ProtectedTime", "ProtectedForSeconds");
        if (num is > 0) { seconds = num.Value; return true; }

        // 2) Minuten → Sekunden
        var mins = IntOf(src, "ProtectedMinutes", "cachedProtectedMinutes", "MinutesRemaining", "TimeRemainingMinutes");
        if (mins is > 0) { seconds = mins.Value * 60; return true; }

        // 3) Unix-Expiry -> jetzt
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = IntOf(src, "ProtectionExpiry", "ExpireTime", "Expiration", "Expiry", "ExpiresAt", "ProtectionExpiresAt");
        if (exp is > 0 && exp.Value > now)
        {
            seconds = (int)(exp.Value - now);
            return true;
        }

        // 4) Stringformen ("1d","12h","45m","90s","1.5d","1 day")
        var s = StrOf(src,
            "Upkeep", "UpkeepTime", "ToolCupboardUpkeep", "TcUpkeep",
            "Remaining", "TimeRemaining", "Protection", "ProtectionTime", "UpkeepString");
        if (!string.IsNullOrWhiteSpace(s))
        {
            var v = s.Trim().ToLowerInvariant();
            static bool TryNum(string txt, out double d) =>
                double.TryParse(txt.Replace(",", "."), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out d);

            if (v.EndsWith("days") || v.EndsWith("day")) v = v.Replace("days", "d").Replace("day", "d");
            if (v.EndsWith("hours") || v.EndsWith("hour")) v = v.Replace("hours", "h").Replace("hour", "h");
            if (v.EndsWith("minutes") || v.EndsWith("minute")) v = v.Replace("minutes", "m").Replace("minute", "m");
            if (v.EndsWith("seconds") || v.EndsWith("second")) v = v.Replace("seconds", "s").Replace("second", "s");

            if (v.EndsWith("d") && TryNum(v[..^1], out var dd)) { seconds = (int)(dd * 86400); return true; }
            if (v.EndsWith("h") && TryNum(v[..^1], out var hh)) { seconds = (int)(hh * 3600); return true; }
            if (v.EndsWith("m") && TryNum(v[..^1], out var mm)) { seconds = (int)(mm * 60); return true; }
            if (v.EndsWith("s") && TryNum(v[..^1], out var ss)) { seconds = (int)ss; return true; }
            if (int.TryParse(v, out var si)) { seconds = si; return true; }
            if (double.TryParse(v, out var sd)) { seconds = (int)sd; return true; }
        }

        // 5) Tiefer suchen: Properties, deren Name upkeep/protect/priv enthält
        var t = src.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var name = p.Name.ToLowerInvariant();
            if (!(name.Contains("upkeep") || name.Contains("protect") || name.Contains("priv")))
                continue;

            object? val = null; try { val = p.GetValue(src); } catch { }
            val = UnpackAnyRecursive(val) ?? val;
            if (val == null || val is string) continue;

            if (TryReadUpkeepSeconds(val, out seconds)) return true;
        }

        return false;
    }

    private static IEnumerable? FindItemsList(object src)
    {
        if (src is null) return null;
        static IEnumerable? AsEnum(object? o) => (o is IEnumerable e && o is not string) ? e : null;

        // Direkt-Felder
        foreach (var name in new[] { "Items", "items", "Slots", "slots", "Contents", "contents" })
            if (AsEnum(TryGetProp(src, name)) is { } e) return e;

        // Häufige Nester
        foreach (var nest in new[] { "Inventory", "inventory", "Container", "container", "Storage", "storage", "Contents", "contents" })
            if (FindItemsList(TryGetProp(src, nest)!) is { } en) return en;

        // Heuristik: irgendeine IEnumerable, deren Elemente Item-ähnlich sind
        foreach (var p in src.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            object? val = null; try { val = p.GetValue(src); } catch { }
            if (AsEnum(val) is not IEnumerable e) continue;

            foreach (var el in e)
            {
                if (el is null) continue;
                var hasId = TryReadIntN(el, "ItemId", "ItemID", "Id") != null;
                var hasAmt = TryReadIntN(el, "Amount", "Quantity", "Count", "Stack") != null;
                if (hasId && hasAmt) return e;
            }
        }
        return null;
    }

    private static int? ReadIntFlexible(object? o, params string[] names)
    {
        if (o == null) return null;
        foreach (var n in names)
        {
            var p = o.GetType().GetProperty(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (p == null) continue;
            var v = p.GetValue(o);
            if (v == null) continue;
            if (v is int i) return i;
            if (v is long l) return unchecked((int)l);
            if (v is uint u) return unchecked((int)u);
            if (int.TryParse(v.ToString(), out var j)) return j;
        }
        return null;
    }

    private static string? ReadStringFlexible(object? o, params string[] names)
    {
        if (o == null) return null;
        foreach (var n in names)
        {
            var p = o.GetType().GetProperty(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
            if (p == null) continue;
            var s = p.GetValue(o)?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static object? RPReadProp(object obj, params string[] names)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n);
            if (p != null) return p.GetValue(obj);
            var f = t.GetField(n);
            if (f != null) return f.GetValue(obj);
        }
        return null;
    }

    private static bool RPTryReadUpkeepSeconds(object data, out int value)
    {
        value = 0;
        var u = RPReadProp(data, "UpkeepSeconds", "Upkeep", "ToolCupboardSeconds");
        if (u == null) return false;
        if (u is int i) { value = i; return true; }
        if (int.TryParse(u.ToString(), out var p)) { value = p; return true; }
        return false;
    }

    private static IEnumerable<object?>? RPFindItemsList(object data)
    {
        // häufige Pfade: Inventory.Items / Items / Content / Inventory
        var cand = new[] { "Items", "Inventory", "Content", "Slots" };
        foreach (var c in cand)
        {
            var v = RPReadProp(data, c);
            if (v is System.Collections.IEnumerable en)
                return en.Cast<object?>();
        }
        // verschachtelt: Inventory.Items
        var inv = RPReadProp(data, "Inventory");
        if (inv != null)
        {
            var v = RPReadProp(inv, "Items", "Slots");
            if (v is System.Collections.IEnumerable en)
                return en.Cast<object?>();
        }
        return null;
    }

    private static int? RPReadIntFlexible(object obj, params string[] names)
    {
        var v = RPReadProp(obj, names);
        if (v == null) return null;
        if (v is int i) return i;
        if (int.TryParse(v.ToString(), out var p)) return p;
        return null;
    }

    // ---------- Any/Reflection Helpers ----------
    private static object? TryGetProp(object? o, params string[] names)
    {
        if (o == null) return null;
        var t = o.GetType();
        foreach (var n in names)
        {
            var p = t.GetProperty(n);
            if (p != null) return p.GetValue(o);
        }
        return null;
    }

    /// <summary>
    /// Entpackt google.protobuf.Any rekursiv:
    /// nutzt Properties "TypeUrl"/"Type" + "Payload" (ByteString/byte[]),
    /// sucht dann im selben Assembly eine Message mit Parser.ParseFrom(byte[]).
    /// </summary>
    private static object? UnpackAnyRecursive(object maybeAny, Action<string>? log = null, int depth = 0)
    {
        if (maybeAny == null) return null;

        var typeUrl = (TryGetProp(maybeAny, "TypeUrl", "Type")?.ToString() ?? "").Trim();
        var payload = TryGetProp(maybeAny, "Payload");
        if (payload == null) { return maybeAny; }

        // ByteString → byte[]
        byte[]? bytes = null;
        var pt = payload.GetType();
        if (pt == typeof(byte[]))
            bytes = (byte[])payload;
        else
            bytes = pt.GetMethod("ToByteArray")?.Invoke(payload, null) as byte[];

        if (bytes == null || bytes.Length == 0)
        {
            log?.Invoke($"[stor] depth{depth}: payload empty");
            return maybeAny;
        }

        // Typname aus TypeUrl herauslösen
        string bareType = typeUrl;
        var slash = bareType.LastIndexOf('/');
        if (slash >= 0 && slash + 1 < bareType.Length) bareType = bareType[(slash + 1)..];

        var asm = maybeAny.GetType().Assembly;
        var types = asm.GetTypes();

        // Kandidat finden
        var target =
            types.FirstOrDefault(t => string.Equals(t.FullName, bareType, StringComparison.Ordinal)) ??
            types.FirstOrDefault(t => string.Equals(t.Name, bareType, StringComparison.Ordinal)) ??
            types.FirstOrDefault(t => !string.IsNullOrEmpty(bareType) &&
                                      t.Name.IndexOf(bareType, StringComparison.OrdinalIgnoreCase) >= 0);

        // Wenn TypeUrl leer war → Brute-Force: alle mit Parser probieren
        if (target == null)
        {
            foreach (var cand in types.Where(t => t.GetProperty("Parser") != null))
            {
                try
                {
                    var parser = cand.GetProperty("Parser")!.GetValue(null);
                    var parseFrom = parser!.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
                    if (parseFrom == null) continue;

                    var msg = parseFrom.Invoke(parser, new object[] { bytes! });
                    var props = msg!.GetType().GetProperties().Select(p => p.Name).ToArray();
                    if (props.Any(n => n is not ("Parser" or "Descriptor")))
                    {
                        log?.Invoke($"[stor] depth{depth}: unpacked brute -> {msg.GetType().FullName}");
                        // Falls erneut Any-ähnlich → rekursiv weiter
                        if (props.Contains("Payload") && (props.Contains("Type") || props.Contains("TypeUrl")))
                            return UnpackAnyRecursive(msg, log, depth + 1);
                        return msg;
                    }
                }
                catch { /* ignorieren */ }
            }

            log?.Invoke($"[stor] depth{depth}: no target type for '{bareType}', returning as-is");
            return maybeAny;
        }

        try
        {
            var parser = target.GetProperty("Parser")?.GetValue(null);
            var parseFrom = parser?.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
            if (parser == null || parseFrom == null)
            {
                log?.Invoke($"[stor] depth{depth}: target '{target.FullName}' has no Parser.ParseFrom");
                return maybeAny;
            }

            var msg = parseFrom.Invoke(parser, new object[] { bytes! });
            log?.Invoke($"[stor] depth{depth}: unpack -> {target.FullName}");

            var props = msg!.GetType().GetProperties().Select(p => p.Name).ToArray();
            if (props.Contains("Payload") && (props.Contains("Type") || props.Contains("TypeUrl")))
                return UnpackAnyRecursive(msg, log, depth + 1);

            return msg;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[stor] depth{depth}: unpack error: {ex.Message}");
            return maybeAny;
        }
    }
    

    private readonly HashSet<uint> _subscribed = new();
    private readonly HashSet<uint> _subOnce = new();

    public async Task EnsureSubOnceAsync(uint entityId)
    {
        bool doWire;
        lock (_subOnce) doWire = _subOnce.Add(entityId); // nur einmal pro Entity
        if (!doWire) return;

        await SubscribeEntityAsync(entityId);  // <-- kein _rust hier
        await PokeEntityAsync(entityId);       // <-- direkt auf this
        _log?.Invoke($"[stor/sub+poke] #{entityId} queued");
    }

    public async Task SubscribeEntityAsync(uint entityId)
    {
        HookEventsIfNeeded();
        if (_api is null) return;
        if (_subscribed.Contains(entityId)) return;

        // 1) Try native AddEntitySubscriptionAsync (falls vorhanden)
        var m = _api.GetType().GetMethod("AddEntitySubscriptionAsync", new[] { typeof(uint) });
        if (m != null)
        {
            var call = m.Invoke(_api, new object[] { entityId });
            if (call is Task t) await t;
            _subscribed.Add(entityId);
            _log?.Invoke($"[storage/sub] native subscribed {entityId}");
            return;
        }

        // 2) Fallback über Contracts
        var asm = typeof(RustPlus).Assembly;
        var reqType = asm.GetTypes().FirstOrDefault(x => x.Name == "AppRequest");
        var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name == "AppEmpty");
        if (reqType != null && emptyType != null)
        {
            var req = Activator.CreateInstance(reqType)!;
            reqType.GetProperty("EntityId")?.SetValue(req, entityId);
            // Kandidaten: AddEntitySubscription / AddEntitySub
            var flag = reqType.GetProperty("AddEntitySubscription") ?? reqType.GetProperty("AddEntitySub");
            if (flag != null)
            {
                flag.SetValue(req, Activator.CreateInstance(emptyType)!);
                var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
                if (send != null)
                {
                    var call = send.Invoke(_api, new object[] { req });
                    if (call is Task t) await t;
                    _subscribed.Add(entityId);
                    _log?.Invoke($"[storage/sub] contract subscribed {entityId}");
                }
            }
        }
    }

    public async Task PokeEntityAsync(uint entityId, CancellationToken ct = default)
    {
        if (_api is null) return;

        // 1) Bestehender Pfad (GetEntityInfoAsync) — lassen wir drin
        var m = _api.GetType().GetMethod("GetEntityInfoAsync", new[] { typeof(uint) })
             ?? _api.GetType().GetMethod("GetEntityInfoAsync", new[] { typeof(uint), typeof(CancellationToken) });

        if (m != null)
        {
            var args = m.GetParameters().Length == 2
                ? new object[] { entityId, ct }
                : new object[] { entityId };
            if (m.Invoke(_api, args) is Task t) await t;
        }

        // 2) Zusatz: Contracts-Poke für Storage
        try
        {
            var asm = typeof(RustPlus).Assembly;
            var reqType = asm.GetTypes().FirstOrDefault(x => x.Name == "AppRequest");
            var emptyType = asm.GetTypes().FirstOrDefault(x => x.Name == "AppEmpty");
            if (reqType != null && emptyType != null)
            {
                var req = Activator.CreateInstance(reqType)!;
                reqType.GetProperty("EntityId")?.SetValue(req, entityId);

                // Kandidaten, die in der Praxis Storage-Events auslösen:
                var flag =
    reqType.GetProperty("GetEntityInfo") ??
    reqType.GetProperty("GetStorageMonitor") ??
    reqType.GetProperty("GetStorage") ??
    reqType.GetProperty("GetContainer") ??
    reqType.GetProperty("GetEntityStorage") ??
    reqType.GetProperty("RequestEntityUpdate") ??
    reqType.GetProperty("PollEntity");       // <— neu

                if (flag != null)
                {
                    _log?.Invoke($"[poke/contracts] using {flag.Name} for {entityId}");
                    flag.SetValue(req, Activator.CreateInstance(emptyType)!);
                    var send = _api.GetType().GetMethod("SendRequestAsync", new[] { reqType });
                    if (send != null)
                    {
                        var call = send.Invoke(_api, new object[] { req });
                        if (call is Task t) await t;
                        return;
                    }
                }
                else
                {
                    _log?.Invoke($"[poke/contracts] no suitable request flag found for {entityId}");
                }
            }
        }
        catch { /* tolerant */ }
    }

    private void LogAllApiEventsOnce()
    {
        try
        {
            var names = _api!.GetType().GetEvents().Select(e => e.Name).OrderBy(n => n).ToArray();
            _log?.Invoke("[stor/sniff] events: " + string.Join(", ", names));
        }
        catch { }
    }

    // generisch: zur Laufzeit die richtige EventHandler<T>-Signatur schließen
    private void AttachSniffer(string eventName)
    {
        try
        {
            var ev = _api!.GetType().GetEvent(eventName);
            if (ev == null) return;

            var tArgs = ev.EventHandlerType!.GetMethod("Invoke")!.GetParameters()[1].ParameterType;
            var m = typeof(RustPlusClientReal).GetMethod(nameof(SniffGeneric),
                     BindingFlags.NonPublic | BindingFlags.Instance)!
                     .MakeGenericMethod(tArgs);

            var del = Delegate.CreateDelegate(ev.EventHandlerType, this, m);
            ev.AddEventHandler(_api, del);
            _log?.Invoke($"[sniff] attached → {eventName}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[sniff] attach failed for {eventName}: {ex.Message}");
        }
    }

    private void SniffGeneric<T>(object? sender, T arg)
{
    var name = typeof(T).Name;
    if (!(name.Contains("Response", StringComparison.OrdinalIgnoreCase) ||
          name.Contains("Entity", StringComparison.OrdinalIgnoreCase) ||
          name.Contains("Storage", StringComparison.OrdinalIgnoreCase)))
        return;

    _log?.Invoke($"[sniff/event] {name}");
    DumpShapeLog(arg, $"evt:{name}");
}

    private void DumpShapeLog(object? o, string label, int maxProps = 20)
    {
        try
        {
            if (o is null) { _log?.Invoke($"[{label}] <null>"); return; }
            var t = o.GetType();
            var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var take = props.Take(maxProps).Select(p =>
            {
                object? v = null;
                try { v = p.GetValue(o); } catch { /* ignore */ }
                string sv =
                    (v is null) ? "null" :
                    (v is string s && s.Length > 80 ? $"\"{s[..80]}…\"" :
                    v is IEnumerable e && !(v is string) ? $"IEnumerable<{v.GetType().Name}>" :
                    v.GetType().IsPrimitive || v is decimal ? v.ToString()! :
                    v.GetType().Name);
                return $"{p.Name}={sv}";
            });
            _log?.Invoke($"[{label}] {t.Name}: " + string.Join(", ", take));
            if (props.Length > maxProps)
                _log?.Invoke($"[{label}] … {props.Length - maxProps} more props");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[{label}] dump err: {ex.Message}");
        }
    }

    private static bool TryGetTaskResult(Task task, out object? result)
    {
        result = null;
        var tt = task.GetType();
        if (!tt.IsGenericType) return false;               // Task (ohne T) → kein Result
        var pi = tt.GetProperty("Result");
        if (pi == null) return false;
        try { result = pi.GetValue(task); return true; }
        catch { return false; }                             // Getter wirft → false
    }

    private void LogTaskException(string tag, Exception ex)
    {
        if (ex is TargetInvocationException tie && tie.InnerException != null)
            _log?.Invoke($"[{tag}] {tie.InnerException.GetType().Name}: {tie.InnerException.Message}\n{tie.InnerException}");
        else
            _log?.Invoke($"[{tag}] {ex.GetType().Name}: {ex.Message}\n{ex}");
    }

    private readonly Dictionary<int, uint> _seqToEntity = new();

    // kleine Reader


    static int? TryReadIntN(object? o, params string[] names)
    {
        if (o == null) return null;
        foreach (var n in names)
        {
            var v = TryGetProp(o, n);
            if (v == null) continue;
            try
            {
                switch (v)
                {
                    case int i: return i;
                    case short s: return (int)s;
                    case byte b: return (int)b;
                    case long l:
                        if (l > int.MaxValue) return int.MaxValue;
                        if (l < int.MinValue) return int.MinValue;
                        return (int)l;
                    case uint u:
                        if (u > int.MaxValue) return int.MaxValue;
                        return (int)u;
                    case ulong ul:
                        if (ul > (ulong)int.MaxValue) return int.MaxValue;
                        return (int)ul;
                    case double d:
                        if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                        var rd = Math.Round(d);
                        if (rd > int.MaxValue) return int.MaxValue;
                        if (rd < int.MinValue) return int.MinValue;
                        return (int)rd;
                    case string s when int.TryParse(s, out var si): return si;
                    case string s2 when long.TryParse(s2, out var sl):
                        if (sl > int.MaxValue) return int.MaxValue;
                        if (sl < int.MinValue) return int.MinValue;
                        return (int)sl;
                }
            }
            catch { /* tolerant */ }
        }
        return null;
    }

    static uint? TryReadUIntN(object? o, params string[] names)
    {
        if (o == null) return null;
        foreach (var n in names)
        {
            var v = TryGetProp(o, n);
            if (v == null) continue;
            try
            {
                switch (v)
                {
                    case uint u: return u;
                    case int i when i >= 0: return (uint)i;
                    case long l when l >= 0: return l > uint.MaxValue ? uint.MaxValue : (uint)l;
                    case ulong ul: return ul > uint.MaxValue ? uint.MaxValue : (uint)ul;
                    case double d when d >= 0 && !double.IsNaN(d) && !double.IsInfinity(d):
                        {
                            var rd = Math.Round(d);
                            if (rd < 0) continue;
                            return rd > uint.MaxValue ? uint.MaxValue : (uint)rd;
                        }
                    case string s when ulong.TryParse(s, out var sul):
                        return sul > uint.MaxValue ? uint.MaxValue : (uint)sul;
                }
            }
            catch { /* tolerant */ }
        }
        return null;
    }

    // praktische 0-Defaults:
    static int ReadIntDef(object? o, int def, params string[] names) => TryReadIntN(o, names) ?? def;
    static uint ReadUIntDef(object? o, uint def, params string[] names) => TryReadUIntN(o, names) ?? def;

    private static string? TryReadStringN(object? o, params string[] names)
    {
        if (o is null) return null;
        foreach (var n in names)
        {
            var v = Prop(o, n);
            if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static bool TryReadDateTimeUtc(object? o, params string[] names)
    {
        foreach (var n in names)
        {
            var v = TryGetProp(o, n);
            if (v is DateTime dt) return true; // RustLib liefert meist UTC
            if (v is string s && DateTime.TryParse(s, out var p)) return true;
            if (v != null && DateTime.TryParse(v.ToString(), out var q)) return true;
        }
        return false;
    }

    private static int? SecondsUntil(object? src, params string[] names)
    {
        object? v = null;
        foreach (var n in names)
        {
            v = TryGetProp(src, n);
            if (v != null) break;
        }
        if (v == null) return null;

        DateTime expiry;
        if (v is DateTime dt) expiry = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        else if (!DateTime.TryParse(v.ToString(), out expiry)) return null;

        var now = DateTime.UtcNow;
        var secs = (int)Math.Max(0, (expiry - now).TotalSeconds);
        return secs;
    }

    private readonly Dictionary<uint, (int items, int sum, int? upkeep)> _lastSig = new();

    private bool HasChanged(uint id, StorageSnap s)
    {
        var sum = s.Items?.Sum(x => (x?.ItemId ?? 0) ^ (x?.Amount ?? 0)) ?? 0;
        var cur = (s.Items?.Count ?? 0, sum, s.UpkeepSeconds);
        if (_lastSig.TryGetValue(id, out var prev) && prev.Equals(cur)) return false;
        _lastSig[id] = cur;
        return true;
    }

    public static string HumanizeUpkeep(int? secs)
    {
        if (secs is null) return "–";
        var s = secs.Value;
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m";
        if (s < 86400) return $"{s / 3600}h";
        return $"{s / 86400}d";
    }

}
