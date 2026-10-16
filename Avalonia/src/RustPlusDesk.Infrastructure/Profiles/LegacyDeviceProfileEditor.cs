using System.Globalization;
using System.Text.Json.Nodes;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Infrastructure.Profiles;

/// <summary>Moves original JSON subtrees so aliases, nested settings and unknown fields survive.</summary>
public sealed class LegacyDeviceProfileEditor : IDeviceProfileEditor
{
    public Task<DeviceEditResult> RenameAsync(string path, ServerSummary original, string deviceKey, string alias, CancellationToken cancellationToken)
        => EditAsync(path, original, deviceKey, (root, selected) =>
        {
            selected.Node["Alias"] = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
            return selected.Node;
        }, cancellationToken);

    public Task<DeviceEditResult> SaveAlarmTitleAsync(string path, ServerSummary original, string deviceKey, string title, CancellationToken cancellationToken)
        => EditAsync(path, original, deviceKey, (root, selected) =>
        {
            string? kind = selected.Node["Kind"]?.GetValue<string>()?.Replace(" ", "", StringComparison.Ordinal);
            if (selected.Node["IsGroup"]?.GetValue<bool>() == true || !string.Equals(kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a smart alarm.");
            selected.Node["InGameAlarmTitle"] = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
            return selected.Node;
        }, cancellationToken);

    public Task<DeviceEditResult> GroupAsync(string path, ServerSummary original, string deviceKey, string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return EditAsync(path, original, deviceKey, (root, selected) =>
        {
            var ids = All(root).Select(node => node["EntityId"]?.GetValue<uint>() ?? 0).ToHashSet();
            uint id;
            do { id = 0x80000000 | (uint)Random.Shared.Next(1, int.MaxValue); } while (ids.Contains(id));
            var group = new JsonObject
            { ["EntityId"] = id, ["Kind"] = "Group", ["IsGroup"] = true, ["Alias"] = name.Trim(), ["IsExpanded"] = true, ["Children"] = new JsonArray() };
            selected.Parent[selected.Index] = group;
            group["Children"]!.AsArray().Add(selected.Node);
            return group;
        }, cancellationToken);
    }

    public Task<DeviceEditResult> MoveAsync(string path, ServerSummary original, string deviceKey, string? parentKey, int? index, CancellationToken cancellationToken)
        => EditAsync(path, original, deviceKey, (root, selected) =>
        {
            JsonArray destination = root;
            if (parentKey is not null)
            {
                var parent = Find(root, parentKey).Node;
                if (parent["IsGroup"]?.GetValue<bool>() != true) throw new ArgumentException("Choose a group.");
                for (JsonNode? node = parent; node is not null; node = node.Parent)
                    if (ReferenceEquals(node, selected.Node)) throw new ArgumentException("A group cannot contain itself.");
                if (parent["Children"] is null) parent["Children"] = new JsonArray();
                destination = parent["Children"]!.AsArray();
                parent["IsExpanded"] = true;
            }
            selected.Parent.RemoveAt(selected.Index);
            int position = index ?? destination.Count;
            if (position < 0 || position > destination.Count) throw new ArgumentException("Choose a valid position.");
            destination.Insert(position, selected.Node);
            return selected.Node;
        }, cancellationToken);

    private static async Task<DeviceEditResult> EditAsync(string path, ServerSummary original, string deviceKey,
        Func<JsonArray, (JsonArray Parent, int Index, JsonObject Node), JsonObject> update, CancellationToken ct)
    {
        int profileIndex = -1;
        string? updatedKey = null;
        var saved = await LegacyProfileStore.UpdateAsync(path, document =>
        {
            var matches = document.Select((node, index) => (Node: node!, Index: index)).Where(pair =>
                LegacyProfileIdentity.Create(pair.Node["Host"]!.GetValue<string>(), pair.Node["Port"]?.GetValue<int>() ?? 28082,
                    pair.Node["SteamId64"]?.GetValue<string>()) == original.ProfileKey).ToArray();
            if (matches.Length != 1) throw new ProfileChangedException();
            profileIndex = matches[0].Index;
            var devices = matches[0].Node["Devices"]?.AsArray() ?? throw new ProfileChangedException();
            var edited = update(devices, Find(devices, deviceKey));
            updatedKey = KeyFor(devices, edited) ?? throw new ProfileChangedException();
        }, original.SourceRevision, ct).ConfigureAwait(false);
        return new(saved[profileIndex], updatedKey!);
    }

    private static (JsonArray Parent, int Index, JsonObject Node) Find(JsonArray root, string key)
    {
        var segments = key.Split('/');
        for (int step = 0; step < segments.Length; step++)
        {
            if (!int.TryParse(segments[step], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index < 0 || index >= root.Count || root[index] is not JsonObject node) throw new ProfileChangedException();
            if (step == segments.Length - 1) return (root, index, node);
            root = node["Children"]?.AsArray() ?? throw new ProfileChangedException();
        }
        throw new ProfileChangedException();
    }

    private static IEnumerable<JsonObject> All(JsonArray nodes)
    {
        foreach (var node in nodes.OfType<JsonObject>())
        {
            yield return node;
            if (node["Children"] is JsonArray children)
                foreach (var child in All(children)) yield return child;
        }
    }

    private static string? KeyFor(JsonArray nodes, JsonObject target, string prefix = "")
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            string key = prefix + i.ToString(CultureInfo.InvariantCulture);
            if (ReferenceEquals(nodes[i], target)) return key;
            if (nodes[i]?["Children"] is JsonArray children && KeyFor(children, target, key + "/") is { } found) return found;
        }
        return null;
    }
}
