using System.Text.Json;
using System.Text.Json.Nodes;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Infrastructure.Storage;

namespace RustPlusDesk.Infrastructure.Accounts;

internal sealed record CompanionConfigFile(JsonObject Values, byte[]? Original)
{
    public string? Text(string key) => Values[key]?.GetValue<string>();

    public static async Task<CompanionConfigFile> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? original = await AtomicPrivateFile.ReadAsync(path, cancellationToken).ConfigureAwait(false);
            return new(original is null ? new() : JsonNode.Parse(original)?.AsObject() ?? throw new JsonException(), original);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { throw new CompanionAccountException(AccountFailure.InvalidConfiguration); }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        try { await AtomicPrivateFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(Values), Original, false, cancellationToken).ConfigureAwait(false); }
        catch (ProfileChangedException) { throw new CompanionAccountException(AccountFailure.ChangedConfiguration); }
    }
}
