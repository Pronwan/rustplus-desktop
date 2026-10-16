using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Core.Recycling;

namespace RustPlusDesk.Avalonia.Features.Recycling;

public sealed class RecyclerItemViewModel(RecyclerItem item, Action changed) : ObservableObject
{
    public RecyclerItem Definition { get; } = item;
    public string Name => Definition.Resource.Name;
    public string IconKey => Definition.Resource.ShortName;
    private int quantity;
    public int Quantity { get => quantity; set { if (SetProperty(ref quantity, Math.Max(0, value))) changed(); } }
    public void Adjust(int delta) => Quantity = (int)Math.Clamp((long)Quantity + delta, 0, int.MaxValue);
}

public sealed record RecyclerOutputViewModel(RecyclerOutput Output)
{
    public string Name => Output.Resource.Name;
    public string IconKey => Output.Resource.ShortName;
    public string WildGuaranteed => "Guar: " + Amount(Output.Wild.Guaranteed);
    public string SafeGuaranteed => "Guar: " + Amount(Output.Safe.Guaranteed);
    public string WildChance => Chance(Output.Wild);
    public string SafeChance => Chance(Output.Safe);
    public string WildRange => Range(Output.Wild);
    public string SafeRange => Range(Output.Safe);
    private static string Amount(double value) => Math.Round(value).ToString("0");
    private static string Chance(RecyclerTotals value) => "Chance: " + Amount(value.ChanceQuantity)
        + (value.ChanceQuantity > 0 ? $" ({Math.Round(value.ChancePercent)}%)" : "");
    private static string Range(RecyclerTotals value) => value.Guaranteed < value.Maximum
        ? $"Range: {value.Guaranteed:0.#} - {value.Maximum:0.#}; expected: {value.Expected:0.#}" : $"Expected: {value.Expected:0.#}";
}

/// <summary>Keeps hidden quantities while filtering and pages only the rendered input tiles.</summary>
public sealed partial class RecyclerViewModel : ObservableObject
{
    private readonly RecyclerCatalog catalog;
    private readonly RecyclerItemViewModel[] all;
    private RecyclerItemViewModel[] filtered = [];
    private bool batching;
    public ObservableCollection<RecyclerItemViewModel> Items { get; } = [];
    public IReadOnlyList<string> Categories { get; }
    [ObservableProperty] private string search = "";
    [ObservableProperty] private string category = "All Categories";
    [ObservableProperty] private IReadOnlyList<RecyclerOutputViewModel> outputs = [];
    [ObservableProperty] private string wildTime = "00:00:00";
    [ObservableProperty] private string safeTime = "00:00:00";
    public bool HasMore => Items.Count < filtered.Length;
    public bool HasOutputs => Outputs.Count > 0;

    public RecyclerViewModel(RecyclerCatalog catalog)
    {
        this.catalog = catalog;
        all = catalog.Items.Select(item => new RecyclerItemViewModel(item, Recalculate)).ToArray();
        Categories = new[] { "All Categories" }.Concat(catalog.Items.Select(item => item.Category)
            .Where(value => !string.IsNullOrEmpty(value)).Distinct().Order()).ToArray();
        Filter();
    }

    partial void OnSearchChanged(string value) => Filter();
    partial void OnCategoryChanged(string value) => Filter();
    private void Filter()
    {
        filtered = all.Where(item => (string.IsNullOrEmpty(Search) || item.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || item.Definition.Resource.ShortName.Contains(Search, StringComparison.OrdinalIgnoreCase))
            && (Category == "All Categories" || item.Definition.Category == Category)).ToArray();
        Items.Clear();
        LoadMore();
    }

    [RelayCommand]
    private void LoadMore()
    {
        int end = Math.Min(Items.Count + 30, filtered.Length);
        for (int i = Items.Count; i < end; i++) Items.Add(filtered[i]);
        OnPropertyChanged(nameof(HasMore));
    }

    [RelayCommand]
    private void Clear() => Batch(all, item => item.Quantity = 0);
    [RelayCommand]
    private void FillStacks() => Batch(Items, item => item.Quantity = item.Definition.StackSize);
    private void Batch(IEnumerable<RecyclerItemViewModel> items, Action<RecyclerItemViewModel> update)
    {
        batching = true;
        try { foreach (var item in items) update(item); }
        finally { batching = false; Recalculate(); }
    }

    private void Recalculate()
    {
        if (batching) return;
        var result = RecyclerCalculator.Calculate(catalog, all.Select(item => (item.Definition, item.Quantity)));
        Outputs = result.Outputs.Select(output => new RecyclerOutputViewModel(output)).ToArray();
        OnPropertyChanged(nameof(HasOutputs));
        WildTime = Time(result.WildSeconds);
        SafeTime = Time(result.SafeSeconds);
    }

    private static string Time(long seconds) => $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
}
