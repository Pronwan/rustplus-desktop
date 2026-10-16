namespace RustPlusDesk.Core.Recycling;

public sealed record RecyclerResource(string ShortName, string Name);
public sealed record RecyclerYield(string ShortName, double Quantity, double Probability);
public sealed record RecyclerItem(RecyclerResource Resource, string Category, int StackSize,
    IReadOnlyList<RecyclerYield> Wild, IReadOnlyList<RecyclerYield> Safe);
public sealed record RecyclerCatalog(IReadOnlyList<RecyclerItem> Items, IReadOnlyDictionary<string, RecyclerResource> Resources);
public interface IRecyclerCatalog { RecyclerCatalog Load(); }

public sealed record RecyclerTotals(double Expected, double Guaranteed, double Maximum)
{
    public double ChanceQuantity => Math.Max(0, Maximum - Guaranteed);
    public double ChancePercent => ChanceQuantity > 0 ? (Expected - Guaranteed) / ChanceQuantity * 100 : 0;
}
public sealed record RecyclerOutput(RecyclerResource Resource, RecyclerTotals Wild, RecyclerTotals Safe);
public sealed record RecyclerResult(IReadOnlyList<RecyclerOutput> Outputs, long WildSeconds, long SafeSeconds);

/// <summary>Reference recycling yields, independent of catalog storage and UI state.</summary>
public static class RecyclerCalculator
{
    public static RecyclerResult Calculate(RecyclerCatalog catalog, IEnumerable<(RecyclerItem Item, int Quantity)> inventory)
    {
        var wild = new Dictionary<string, RecyclerTotals>(StringComparer.OrdinalIgnoreCase);
        var safe = new Dictionary<string, RecyclerTotals>(StringComparer.OrdinalIgnoreCase);
        long ticks = 0;
        foreach (var (item, quantity) in inventory)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(quantity);
            if (quantity == 0) continue;
            int unitsPerTick = Math.Max(1, (int)Math.Ceiling(item.StackSize * 0.10));
            ticks = checked(ticks + ((long)quantity + unitsPerTick - 1) / unitsPerTick);
            Add(wild, item.Wild, quantity);
            Add(safe, item.Safe, quantity);
        }
        var outputs = wild.Keys.Union(safe.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Where(key => wild.GetValueOrDefault(key)?.Expected > 0 || safe.GetValueOrDefault(key)?.Expected > 0)
            .Select(key => new RecyclerOutput(catalog.Resources.GetValueOrDefault(key) ?? new(key, key),
                wild.GetValueOrDefault(key) ?? new(0, 0, 0), safe.GetValueOrDefault(key) ?? new(0, 0, 0))).ToArray();
        return new(outputs, checked(ticks * 5), checked(ticks * 8));
    }

    private static void Add(Dictionary<string, RecyclerTotals> totals, IEnumerable<RecyclerYield> yields, int count)
    {
        foreach (var yield in yields)
        {
            var previous = totals.GetValueOrDefault(yield.ShortName) ?? new(0, 0, 0);
            double maximum = count * yield.Quantity;
            totals[yield.ShortName] = new(previous.Expected + maximum * yield.Probability,
                previous.Guaranteed + (yield.Probability >= 1 ? maximum : 0), previous.Maximum + maximum);
        }
    }
}
