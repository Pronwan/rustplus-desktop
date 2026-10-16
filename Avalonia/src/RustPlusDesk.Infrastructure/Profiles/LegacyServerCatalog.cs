using System.Text.Json;
using System.Security.Cryptography;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Devices;

namespace RustPlusDesk.Infrastructure.Profiles;

/// <summary>Projects the existing profiles.json format into credential-free server descriptions.</summary>
public sealed class LegacyServerCatalog : IServerCatalog
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk", "profiles.json");

    public async Task<IReadOnlyList<ServerSummary>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
    }

    internal static IReadOnlyList<ServerSummary> Parse(byte[] bytes)
    {
        var profiles = JsonSerializer.Deserialize<List<LegacyProfile?>>(bytes)
            ?? throw new JsonException("The profiles document must be an array.");

        var servers = new List<ServerSummary>(profiles.Count);
        string revision = Convert.ToHexString(SHA256.HashData(bytes));
        foreach (var profile in profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Host) || profile.Port is < 1 or > 65535)
                throw new JsonException("A server profile has an invalid host or port.");

            servers.Add(new ServerSummary(
                string.IsNullOrWhiteSpace(profile.Name) ? profile.Host : profile.Name, profile.Host, profile.Port)
            {
                ProfileKey = LegacyProfileIdentity.Create(profile.Host, profile.Port, profile.SteamId64),
                SourceRevision = revision,
                Devices = MapDevices(profile.Devices)
            });
        }
        return servers;
    }

    private static IReadOnlyList<PairedDevice> MapDevices(List<LegacyDevice?>? devices, string prefix = "")
    {
        if (devices is null) return [];
        return devices.Select((device, index) =>
        {
            if (device is null) throw new JsonException("A paired device is invalid.");
            string kind = device.Kind?.Replace(" ", "", StringComparison.Ordinal) ?? "";
            var type = device.IsGroup ? DeviceKind.Group : kind.ToLowerInvariant() switch
            {
                "smartswitch" => DeviceKind.Switch,
                "smartalarm" => DeviceKind.Alarm,
                "storagemonitor" => DeviceKind.StorageMonitor,
                _ => DeviceKind.Unknown
            };
            string name = !string.IsNullOrWhiteSpace(device.Alias) ? device.Alias
                : !string.IsNullOrWhiteSpace(device.Name) ? device.Name : device.Kind ?? "Device";
            string key = prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new PairedDevice(device.EntityId, name, type)
            { DeviceKey = key, Alias = device.Alias, InGameAlarmTitle = device.InGameAlarmTitle, Children = MapDevices(device.Children, key + "/") };
        }).ToArray();
    }

    // Only display fields are materialized; player tokens and device secrets stay out of UI models.
    private sealed class LegacyProfile
    {
        public string? Name { get; init; }
        public string? Host { get; init; }
        public int Port { get; init; } = 28082;
        public string? SteamId64 { get; init; }
        public List<LegacyDevice?>? Devices { get; init; }
    }

    private sealed class LegacyDevice
    {
        public uint EntityId { get; init; }
        public string? Name { get; init; }
        public string? Alias { get; init; }
        public string? InGameAlarmTitle { get; init; }
        public string? Kind { get; init; }
        public bool IsGroup { get; init; }
        public List<LegacyDevice?>? Children { get; init; }
    }
}
