using System.Text.Json;

namespace RustPlusDesk.Infrastructure.Items;

/// <summary>Uses the same packaged item names as the reference app, without UI or network dependencies.</summary>
public static class BundledItemCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<int, string>> Names = new(Load);

    public static string GetName(int id) => Names.Value.TryGetValue(id, out string? name) ? name : $"Item #{id}";

    private static IReadOnlyDictionary<int, string> Load()
    {
        using var stream = typeof(BundledItemCatalog).Assembly.GetManifestResourceStream("RustPlusDesk.Infrastructure.Items.json")
            ?? throw new InvalidDataException("The packaged item catalog is missing.");
        using var document = JsonDocument.Parse(stream);
        var names = new Dictionary<int, string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            int id = item.GetProperty("id").GetInt32();
            string? name = item.GetProperty("displayName").GetString();
            if (!string.IsNullOrWhiteSpace(name)) names[id] = name;
        }
        if (names.Count == 0) throw new InvalidDataException("The packaged item catalog is empty.");
        return names;
    }
}
