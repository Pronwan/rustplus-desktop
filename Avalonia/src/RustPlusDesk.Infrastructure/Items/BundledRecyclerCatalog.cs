using System.Globalization;
using System.Text.Json;
using RustPlusDesk.Core.Recycling;

namespace RustPlusDesk.Infrastructure.Items;

/// <summary>Loads the reference catalogs offline, giving detailed recycling data precedence.</summary>
public sealed class BundledRecyclerCatalog : IRecyclerCatalog
{
    private static readonly Lazy<RecyclerCatalog> Catalog = new(Read);
    public RecyclerCatalog Load() => Catalog.Value;

    private static JsonDocument Json(string name)
    {
        using var stream = typeof(BundledRecyclerCatalog).Assembly.GetManifestResourceStream("RustPlusDesk.Infrastructure." + name)
            ?? throw new InvalidDataException("The bundled recycling catalog is missing.");
        return JsonDocument.Parse(stream);
    }

    private static RecyclerCatalog Read()
    {
        using var names = Json("Items.json");
        using var items = Json("RecyclerItems.json");
        using var details = Json("RecyclingData.json");
        var resources = new Dictionary<string, RecyclerResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in names.RootElement.EnumerateArray())
        {
            string shortName = entry.GetProperty("shortName").GetString()!;
            resources[shortName] = new(shortName, entry.GetProperty("displayName").GetString()!);
        }
        foreach (var entry in items.RootElement.EnumerateArray())
        {
            string shortName = entry.GetProperty("shortName").GetString()!;
            string? name = entry.GetProperty("displayName").GetString();
            if (!string.IsNullOrWhiteSpace(name)) resources[shortName] = new(shortName, name);
        }
        var prettyNames = new Dictionary<string, string>
        {
            ["scrap"] = "Scrap",
            ["metal.refined"] = "High Quality Metal",
            ["metal.fragments"] = "Metal Fragments",
            ["cloth"] = "Cloth",
            ["rope"] = "Rope",
            ["techparts"] = "Tech Parts",
            ["wood"] = "Wood",
            ["stones"] = "Stones",
            ["sulfur"] = "Sulfur",
            ["gunpowder"] = "Gunpowder",
            ["leather"] = "Leather",
            ["fat.animal"] = "Animal Fat",
            ["lowgradefuel"] = "Low Grade Fuel",
            ["bone.fragments"] = "Bone Fragments",
            ["charcoal"] = "Charcoal",
            ["crude.oil"] = "Crude Oil",
            ["riflebody"] = "Rifle Body",
            ["semibody"] = "Semi Body",
            ["smgbody"] = "SMG Body",
            ["metalpipe"] = "Metal Pipe",
            ["metalspring"] = "Metal Spring",
            ["gears"] = "Gears",
            ["metalblade"] = "Metal Blade",
            ["roadsigns"] = "Road Signs",
            ["sheetmetal"] = "Sheet Metal",
            ["sewingkit"] = "Sewing Kit",
            ["tarp"] = "Tarp",
            ["propanetank"] = "Propane Tank",
            ["cctv.camera"] = "CCTV Camera",
            ["targeting.computer"] = "Targeting Computer",
            ["fuse"] = "Fuse"
        };
        foreach (var (key, name) in prettyNames) resources[key] = new(key, name);
        var result = new List<RecyclerItem>();
        foreach (var entry in items.RootElement.EnumerateArray().Where(item => item.GetProperty("canBeRecycled").GetBoolean()))
        {
            string shortName = entry.GetProperty("shortName").GetString()!;
            int stack = Math.Max(1, entry.GetProperty("stackSize").GetInt32());
            var wild = new List<RecyclerYield>();
            var safe = new List<RecyclerYield>();
            if (details.RootElement.TryGetProperty(shortName, out var detailed)
                && (detailed.TryGetProperty("recycler", out _) || detailed.TryGetProperty("safe-zone-recycler", out _)))
            {
                if (detailed.TryGetProperty("stackSize", out var size)) stack = Math.Max(1, size.GetInt32());
                Detailed(detailed, "recycler", wild);
                Detailed(detailed, "safe-zone-recycler", safe);
            }
            else
            {
                // A detailed stack size still overrides the legacy value when only yields are absent.
                if (detailed.ValueKind == JsonValueKind.Object && detailed.TryGetProperty("stackSize", out var size)) stack = Math.Max(1, size.GetInt32());
                foreach (var recycling in entry.GetProperty("recycleInfo").EnumerateArray())
                {
                    var target = recycling.GetProperty("recyclerId").GetString() switch
                    { "recycler-radtown" => wild, "recycler-safezone" => safe, _ => null };
                    if (target is null) continue;
                    foreach (var output in recycling.GetProperty("guaranteedOutput").EnumerateArray())
                        target.Add(new(output.GetProperty("itemId").GetString()!, output.GetProperty("amount").GetDouble(), 1));
                    foreach (var output in recycling.GetProperty("percentageBasedOutput").EnumerateArray())
                        target.Add(new(output.GetProperty("itemId").GetString()!, 1, output.GetProperty("amount").GetDouble() / 100));
                }
            }
            foreach (var yield in wild.Concat(safe))
                if (!resources.ContainsKey(yield.ShortName)) resources[yield.ShortName] = new(yield.ShortName,
                    CultureInfo.CurrentCulture.TextInfo.ToTitleCase(yield.ShortName.Replace('.', ' ').Replace('_', ' ')));
            var resource = entry.GetProperty("displayName").GetString() is { Length: > 0 } displayName
                ? resources[shortName] with { Name = displayName } : resources[shortName];
            result.Add(new(resource, entry.GetProperty("category").GetString() ?? "", stack, wild, safe));
        }
        return new(result.OrderBy(item => item.Resource.Name).ToArray(), resources);
    }

    private static void Detailed(JsonElement node, string key, List<RecyclerYield> target)
    {
        if (!node.TryGetProperty(key, out var recycler) || !recycler.TryGetProperty("yield", out var yields)) return;
        foreach (var entry in yields.EnumerateArray())
            target.Add(new(entry.GetProperty("shortname").GetString()!, entry.GetProperty("quantity").GetDouble(), entry.GetProperty("probability").GetDouble()));
    }
}
