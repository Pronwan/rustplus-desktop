using RustPlusDesk.Core.Servers;

namespace RustPlusDesk.Infrastructure.Profiles;

/// <summary>Updates editable fields using the same revision-protected writer as incoming pairings.</summary>
public sealed class LegacyServerProfileEditor : IServerProfileEditor
{
    public async Task<ServerSummary> SaveAsync(string path, ServerSummary original, string name, ServerAddress address, CancellationToken cancellationToken)
    {
        address.Validate();
        int selected = -1;
        var servers = await LegacyProfileStore.UpdateAsync(path, document =>
        {
            int[] matches = document.Select((node, index) => (node, index)).Where(pair =>
                LegacyProfileIdentity.Create(pair.node!["Host"]!.GetValue<string>(), pair.node["Port"]?.GetValue<int>() ?? 28082,
                    pair.node["SteamId64"]?.GetValue<string>()) == original.ProfileKey).Select(pair => pair.index).ToArray();
            if (matches.Length != 1) throw new ProfileChangedException();
            selected = matches[0];
            var node = document[selected]!.AsObject();
            if (!string.IsNullOrWhiteSpace(name)) node["Name"] = name.Trim();
            node["Host"] = address.Host.Trim();
            node["Port"] = address.Port;
        }, original.SourceRevision, cancellationToken).ConfigureAwait(false);
        return servers[selected];
    }
}
