using System.Globalization;
using System.Text.Json.Nodes;
using RustPlusApi.Fcm.Data;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Infrastructure.Profiles;

/// <summary>Consumes secret-bearing SDK pairings without exposing them outside infrastructure.</summary>
internal static class LegacyPairingWriter
{
    internal sealed record Result(ServerSummary Server, string? PreviousProfileKey);

    public static async Task<Result> SaveAsync(string path, Body body, CancellationToken ct,
        Func<Body, IReadOnlyList<uint>, CancellationToken, Task<bool>>? verify = null)
    {
        new ServerAddress(body.Ip, body.Port).Validate();
        if (body.PlayerId == 0 || !int.TryParse(body.PlayerToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || body.EntityId is 0 or > uint.MaxValue)
            throw new ArgumentException("The pairing credentials or device identity are invalid.");
        string steam = body.PlayerId.ToString(CultureInfo.InvariantCulture);
        string host = body.Ip.Trim();
        string? id = body.Id == Guid.Empty ? null : body.Id.ToString("D");
        var legacyMatch = await LegacyPairingMatcher.FindAsync(path, body, verify ?? PairingConnectionVerifier.VerifyAsync, ct).ConfigureAwait(false);
        int selected = -1;
        string? previousKey = null;
        var servers = await LegacyProfileStore.UpdateAsync(path, profiles =>
        {
            var account = profiles.OfType<JsonObject>().Where(node => Text(node, "SteamId64") == steam).ToArray();
            var byId = id is null ? [] : account.Where(node => string.Equals(Text(node, "ServerId"), id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var byAddress = account.Where(node => string.Equals(Text(node, "Host"), host, StringComparison.OrdinalIgnoreCase)
                && (node["Port"]?.GetValue<int>() ?? 28082) == body.Port).ToArray();
            if (byId.Length > 1 || byAddress.Length > 1) throw new ProfileChangedException();
            var profile = byId.SingleOrDefault() ?? byAddress.SingleOrDefault()
                ?? account.SingleOrDefault(node => legacyMatch is not null && LegacyProfileIdentity.Create(Text(node, "Host")!, node["Port"]?.GetValue<int>() ?? 28082, steam) == legacyMatch.ProfileKey);
            if (profile is not null && id is not null && Guid.TryParse(Text(profile, "ServerId"), out var existing) && existing != Guid.Empty && existing != body.Id)
                throw new ProfileChangedException();
            // Keep ambiguous legacy profiles rather than merging a different server at the same host.
            if (profile is null && account.Any(node => string.Equals(Text(node, "Host"), host, StringComparison.OrdinalIgnoreCase)))
                throw new ProfileChangedException();
            if (profile is null)
            {
                profile = new JsonObject { ["Devices"] = new JsonArray(), ["UseFacepunchProxy"] = false };
                profiles.Add(profile);
            }
            else previousKey = LegacyProfileIdentity.Create(Text(profile, "Host")!, profile["Port"]?.GetValue<int>() ?? 28082, steam);
            selected = profiles.IndexOf(profile);
            profile["Host"] = host;
            profile["Port"] = body.Port;
            profile["SteamId64"] = steam;
            profile["PlayerToken"] = body.PlayerToken;
            if (!string.IsNullOrWhiteSpace(body.Name)) profile["Name"] = body.Name;
            else if (string.IsNullOrWhiteSpace(Text(profile, "Name"))) profile["Name"] = $"{host}:{body.Port}";
            if (id is not null) profile["ServerId"] = id;
            if (string.IsNullOrWhiteSpace(Text(profile, "Description")) && !string.IsNullOrWhiteSpace(body.Desc))
                profile["Description"] = body.Desc.Trim();
            if (body.EntityId is { } entity)
            {
                var devices = profile["Devices"] as JsonArray;
                if (profile["Devices"] is not null && devices is null) throw new ProfileChangedException();
                if (devices is null) profile["Devices"] = devices = new();
                var device = FindDevice(devices, (uint)entity);
                bool isNew = device is null;
                if (isNew) { device = new JsonObject { ["EntityId"] = (uint)entity }; devices.Add(device); }
                string kind = DeviceKindOf(body);
                if (!string.Equals(Text(device!, "Kind"), "SmartAlarm", StringComparison.OrdinalIgnoreCase)) device!["Kind"] = kind;
                if (!string.IsNullOrWhiteSpace(body.EntityName)) device!["Name"] = body.EntityName;
                else if (isNew) device!["Name"] = "Smart Device";
                device!["IsMissing"] = false;
                if (isNew || Text(device, "Kind") == "StorageMonitor") device["IsOn"] = kind == "StorageMonitor" ? null : false;
            }
        }, legacyMatch?.SourceRevision, ct).ConfigureAwait(false);
        return new(servers[selected], previousKey);
    }

    private static JsonObject? FindDevice(JsonArray devices, uint id)
    {
        foreach (var device in devices.OfType<JsonObject>())
        {
            if (device["EntityId"]?.GetValue<uint>() == id && device["IsGroup"]?.GetValue<bool>() != true) return device;
            if (device["Children"] is JsonArray children && FindDevice(children, id) is { } match) return match;
        }
        return null;
    }

    public static async Task<bool> UpdateMetadataAsync(string path, Body body, CancellationToken ct)
    {
        if (body.Id == Guid.Empty && string.IsNullOrWhiteSpace(body.Desc)) return false;
        bool changed = false;
        await LegacyProfileStore.UpdateAsync(path, profiles =>
        {
            foreach (var profile in profiles.OfType<JsonObject>())
            {
                bool knownId = Guid.TryParse(Text(profile, "ServerId"), out var existing) && existing != Guid.Empty;
                bool sameId = body.Id != Guid.Empty && knownId && existing == body.Id;
                bool sameAddress = string.Equals(Text(profile, "Host")?.Trim(), body.Ip?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && (profile["Port"]?.GetValue<int>() ?? 28082) == body.Port;
                if (!sameId && !(sameAddress && (body.Id == Guid.Empty || !knownId))) continue;
                if (!knownId && body.Id != Guid.Empty) { profile["ServerId"] = body.Id.ToString("D"); changed = true; }
                if (string.IsNullOrWhiteSpace(Text(profile, "Description")) && !string.IsNullOrWhiteSpace(body.Desc))
                { profile["Description"] = body.Desc.Trim(); changed = true; }
            }
        }, null, ct).ConfigureAwait(false);
        return changed;
    }

    private static string DeviceKindOf(Body body)
    {
        if (body.EntityType is 1) return "SmartSwitch";
        if (body.EntityType is 2) return "SmartAlarm";
        if (body.EntityType is 3) return "StorageMonitor";
        string type = body.Type ?? "";
        string name = body.EntityName ?? "";
        if (type.Contains("alarm", StringComparison.OrdinalIgnoreCase)) return "SmartAlarm";
        if (type.Contains("switch", StringComparison.OrdinalIgnoreCase)) return "SmartSwitch";
        if (type.Contains("storage", StringComparison.OrdinalIgnoreCase)) return "StorageMonitor";
        if (name.Contains("alarm", StringComparison.OrdinalIgnoreCase)) return "SmartAlarm";
        if (new[] { "storage", "monitor", "cupboard" }
            .Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))) return "StorageMonitor";
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\btc\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "StorageMonitor";
        return "SmartSwitch";
    }

    private static string? Text(JsonObject node, string property) => node[property]?.GetValue<string>();
}
