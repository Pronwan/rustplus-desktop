using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusDesk.Models.Raid;
using RustPlusDesk.Services.Raid;
using RustPlusDesk.Views;

namespace RustPlusDesk.ViewModels;

public sealed class RaidCalculatorViewModel : INotifyPropertyChanged
{
    private readonly RaidDataService _dataService = new();
    private readonly RaidPlanStore _planStore = new();
    private RaidDataSet? _data;
    private RaidCalculatorEngine? _engine;
    private List<RaidTargetCardViewModel> _allTargets = [];
    private string _searchText = string.Empty;
    private string _selectedCategory = "All targets";
    private RaidComparisonMode _comparisonMode = RaidComparisonMode.LowestSulfur;
    private RaidGlobalMethodChoice? _selectedGlobalMethod;
    private string _statusMessage = "Loading raid data…";
    private bool _isLoading = true;
    private bool _showDetails = true;
    private CancellationTokenSource? _saveCancellation;

    public ObservableCollection<RaidTargetCardViewModel> FilteredTargets { get; } = [];
    public ObservableCollection<RaidPlanItemViewModel> PlanItems { get; } = [];
    public ObservableCollection<RaidItemTotalViewModel> BoomTotals { get; } = [];
    public ObservableCollection<RaidResourceTotalViewModel> ResourceTotals { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<RaidGlobalMethodChoice> GlobalMethods { get; } = [];

    public IReadOnlyList<RaidComparisonChoice> ComparisonModes { get; } =
    [
        new(RaidComparisonMode.LowestSulfur, "Lowest sulfur"),
        new(RaidComparisonMode.LowestTotalResources, "Lowest total resources"),
        new(RaidComparisonMode.FewestRaidItems, "Fewest raid items"),
        new(RaidComparisonMode.Custom, "Custom methods")
    ];

    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetField(ref _selectedCategory, value ?? "All targets")) ApplyFilter(); }
    }

    public RaidComparisonChoice SelectedComparison
    {
        get => ComparisonModes.First(choice => choice.Mode == _comparisonMode);
        set
        {
            if (value is null || _comparisonMode == value.Mode) return;
            _comparisonMode = value.Mode;
            OnPropertyChanged();
            SelectGlobalMethodWithoutApplying(GlobalMethods.FirstOrDefault());
            if (_comparisonMode != RaidComparisonMode.Custom)
                foreach (RaidPlanItemViewModel item in PlanItems) item.ApplyRecommendation(_comparisonMode);
            Recalculate();
        }
    }

    public RaidGlobalMethodChoice? SelectedGlobalMethod
    {
        get => _selectedGlobalMethod;
        set
        {
            if (value is null || _selectedGlobalMethod == value) return;
            _selectedGlobalMethod = value;
            OnPropertyChanged();
            if (value.Source is null)
            {
                StatusMessage = string.Empty;
                return;
            }

            _comparisonMode = RaidComparisonMode.Custom;
            OnPropertyChanged(nameof(SelectedComparison));
            int applied = 0;
            foreach (RaidPlanItemViewModel item in PlanItems)
                if (item.SelectSource(value.Source.SourceId)) applied++;
            int unsupported = PlanItems.Count - applied;
            StatusMessage = unsupported == 0
                ? $"Using {value.DisplayName} for the whole raid plan."
                : $"Using {value.DisplayName} for {applied} targets; {unsupported} do not support it in the dataset.";
            Recalculate();
        }
    }

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public bool IsLoading { get => _isLoading; private set { if (SetField(ref _isLoading, value)) OnPropertyChanged(nameof(IsReady)); } }
    public bool IsReady => !IsLoading && _data is not null;
    public bool HasPlan => PlanItems.Count > 0;
    public bool IsPlanEmpty => !HasPlan;
    public bool HasUnavailableCosts => PlanItems.Any(item => item.SelectedMethod is { HasCraftCost: false });
    public bool ShowDetails { get => _showDetails; set => SetField(ref _showDetails, value); }
    public int TargetCount => PlanItems.Sum(item => item.Quantity);
    public int RaidItemCount => PlanItems.Sum(item => item.SelectedMethod?.RequiredItems ?? 0);
    public string PlanCountCaption => $"{TargetCount} target{(TargetCount == 1 ? string.Empty : "s")}  ·  {RaidItemCount} raid item{(RaidItemCount == 1 ? string.Empty : "s")}";
    public string SmartMixCaption
    {
        get
        {
            int count = GlobalMethods.Count(method => method.IsSelected);
            return count == 0 ? "Select raid items" : $"Smart mix · {count} selected";
        }
    }
    public string DataCaption => _data is null ? string.Empty : $"{_data.Targets.Count} targets · {_data.Sources.Count} methods · dataset {_data.GeneratedAt:yyyy-MM-dd}";

    public async Task InitializeAsync()
    {
        try
        {
            _data = await _dataService.LoadAsync();
            _engine = new RaidCalculatorEngine(_data);
            GlobalMethods.Clear();
            GlobalMethods.Add(new RaidGlobalMethodChoice(null));
            foreach (RaidSource source in _data.Sources.OrderBy(source => source.DisplayName))
                GlobalMethods.Add(new RaidGlobalMethodChoice(source)
                {
                    IsSelected = source.ItemShortname is "explosive.timed" or "ammo.rocket.basic" or "ammo.rocket.hv" or "ammo.rifle.explosive"
                });
            _selectedGlobalMethod = GlobalMethods[0];
            OnPropertyChanged(nameof(SelectedGlobalMethod));
            OnPropertyChanged(nameof(SmartMixCaption));
            _allTargets = _data.Targets
                .OrderBy(TargetSortPriority).ThenBy(target => target.DisplayName)
                .Select(target => new RaidTargetCardViewModel(target))
                .ToList();

            Categories.Clear();
            Categories.Add("All targets");
            foreach (string category in _allTargets.Select(target => target.Category).Distinct())
                Categories.Add(category);
            ApplyFilter();

            IReadOnlyList<RaidPlanEntry> saved = await _planStore.LoadAsync();
            foreach (RaidPlanEntry entry in saved)
            {
                RaidTarget? target = _data.Targets.FirstOrDefault(candidate => candidate.TargetId == entry.TargetId);
                if (target is not null && entry.Quantity > 0)
                    AddPlanItem(target, entry.Quantity, entry.SourceId, save: false);
            }
            StatusMessage = _allTargets.Count == 0 ? "No raid targets are available." : string.Empty;
            OnPropertyChanged(nameof(DataCaption));
            Recalculate(save: false);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Raid calculator unavailable: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void AddTarget(RaidTargetCardViewModel target)
    {
        RaidPlanItemViewModel? existing = PlanItems.FirstOrDefault(item => item.Target.TargetId == target.Target.TargetId);
        if (existing is not null)
            existing.Quantity++;
        else
            AddPlanItem(target.Target, 1, _selectedGlobalMethod?.Source?.SourceId ?? 0);
    }

    public void SetSourceSelected(RaidGlobalMethodChoice choice, bool selected)
    {
        if (choice.Source is null) return;
        choice.IsSelected = selected;
        OnPropertyChanged(nameof(SmartMixCaption));
        IReadOnlyList<long> sourceIds = GlobalMethods.Where(method => method.IsSelected && method.Source is not null)
            .Select(method => method.Source!.SourceId).ToList();
        RaidComparisonMode mode = _comparisonMode == RaidComparisonMode.Custom ? RaidComparisonMode.LowestSulfur : _comparisonMode;
        foreach (RaidPlanItemViewModel item in PlanItems) item.ApplySmartMix(sourceIds, mode);
        StatusMessage = sourceIds.Count == 0
            ? "Smart mix disabled; choose a method on each target."
            : $"Smart search is using {sourceIds.Count} selected raid item{(sourceIds.Count == 1 ? string.Empty : "s")}.";
        Recalculate();
    }

    public void Duplicate(RaidPlanItemViewModel item) =>
        AddPlanItem(item.Target, item.Quantity, item.SelectedMethod?.Source.SourceId ?? 0);

    public void Remove(RaidPlanItemViewModel item)
    {
        item.Changed -= PlanItemChanged;
        PlanItems.Remove(item);
        Recalculate();
    }

    public void Clear()
    {
        foreach (RaidPlanItemViewModel item in PlanItems) item.Changed -= PlanItemChanged;
        PlanItems.Clear();
        Recalculate();
    }

    public string BuildSummary()
    {
        var text = new StringBuilder("**Rust raid plan**");
        foreach (RaidPlanItemViewModel item in PlanItems)
        {
            string method = item.SelectedMethod?.DisplayText ?? "no valid method";
            text.AppendLine().Append("• ").Append(item.Target.DisplayName).Append(" ×").Append(item.Quantity).Append(" — ").Append(method);
        }
        if (BoomTotals.Count > 0)
        {
            text.AppendLine().AppendLine().AppendLine("**Boom required**");
            foreach (RaidItemTotalViewModel total in BoomTotals)
                text.Append("• ").Append(total.DisplayName).Append(": ").AppendLine(total.AmountText);
        }
        if (ResourceTotals.Count > 0)
        {
            text.AppendLine().AppendLine().AppendLine("**Crafting resources**");
            foreach (RaidResourceTotalViewModel total in ResourceTotals)
                text.Append("• ").Append(total.DisplayName).Append(": ").AppendLine(total.AmountText);
        }
        if (HasUnavailableCosts)
            text.AppendLine().Append("⚠ Some selected methods have no crafting cost in the dataset.");
        return text.ToString();
    }

    private void AddPlanItem(RaidTarget target, int quantity, long selectedSourceId, bool save = true)
    {
        if (_engine is null) return;
        var item = new RaidPlanItemViewModel(target, _engine, quantity, selectedSourceId, _comparisonMode);
        IReadOnlyList<long> smartSources = GlobalMethods.Where(method => method.IsSelected && method.Source is not null)
            .Select(method => method.Source!.SourceId).ToList();
        if (smartSources.Count > 0)
            item.ApplySmartMix(smartSources, _comparisonMode == RaidComparisonMode.Custom ? RaidComparisonMode.LowestSulfur : _comparisonMode);
        item.Changed += PlanItemChanged;
        PlanItems.Add(item);
        Recalculate(save);
    }

    private void PlanItemChanged(object? sender, EventArgs e)
    {
        if (sender is RaidPlanItemViewModel item && item.UserSelectedMethod)
        {
            item.UserSelectedMethod = false;
            _comparisonMode = RaidComparisonMode.Custom;
            OnPropertyChanged(nameof(SelectedComparison));
            SelectGlobalMethodWithoutApplying(GlobalMethods.FirstOrDefault());
        }
        Recalculate();
    }

    private void SelectGlobalMethodWithoutApplying(RaidGlobalMethodChoice? choice)
    {
        if (choice is null || _selectedGlobalMethod == choice) return;
        _selectedGlobalMethod = choice;
        OnPropertyChanged(nameof(SelectedGlobalMethod));
    }

    private void ApplyFilter()
    {
        IEnumerable<RaidTargetCardViewModel> targets = _allTargets;
        if (_selectedCategory != "All targets")
            targets = targets.Where(target => target.Category == _selectedCategory);
        if (!string.IsNullOrWhiteSpace(_searchText))
            targets = targets.Where(target => target.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase));

        FilteredTargets.Clear();
        foreach (RaidTargetCardViewModel target in targets) FilteredTargets.Add(target);
        OnPropertyChanged(nameof(IsCatalogueEmpty));
    }

    public bool IsCatalogueEmpty => FilteredTargets.Count == 0;

    private static int TargetSortPriority(RaidTarget target) => target.Category switch
    {
        "Building structures" when target.DisplayName.Contains("Foundation", StringComparison.OrdinalIgnoreCase) => 0,
        "Building structures" when target.DisplayName.Contains("Wall", StringComparison.OrdinalIgnoreCase) => 1,
        "Building structures" when target.DisplayName.Contains("Floor", StringComparison.OrdinalIgnoreCase) => 2,
        "Building structures" when target.DisplayName.Contains("Roof", StringComparison.OrdinalIgnoreCase) => 3,
        "Building structures" => 4,
        "Doors & gates" => 5,
        "Barricades" => 6,
        "Traps" => 7,
        "Deployables" => 8,
        _ => 9
    };

    private void Recalculate(bool save = true)
    {
        IReadOnlyList<RaidMethodResult> selectedMethods = PlanItems
            .SelectMany(item => item.SelectedMethod?.Results ?? []).ToList();
        BoomTotals.Clear();
        foreach (RaidItemTotal item in RaidCalculatorEngine.AggregateItems(selectedMethods))
            BoomTotals.Add(new RaidItemTotalViewModel(item));
        ResourceTotals.Clear();
        foreach (RaidResourceTotal resource in RaidCalculatorEngine.Aggregate(selectedMethods))
            ResourceTotals.Add(new RaidResourceTotalViewModel(resource));

        OnPropertyChanged(nameof(HasPlan));
        OnPropertyChanged(nameof(IsPlanEmpty));
        OnPropertyChanged(nameof(HasUnavailableCosts));
        OnPropertyChanged(nameof(TargetCount));
        OnPropertyChanged(nameof(RaidItemCount));
        OnPropertyChanged(nameof(PlanCountCaption));
        if (save) QueueSave();
    }

    private void QueueSave()
    {
        _saveCancellation?.Cancel();
        _saveCancellation = new CancellationTokenSource();
        CancellationToken token = _saveCancellation.Token;
        IReadOnlyList<RaidPlanEntry> snapshot = PlanItems
            .Select(item => new RaidPlanEntry(item.Target.TargetId, item.Quantity, item.SelectedMethod?.SingleSourceId ?? 0))
            .ToList();
        _ = SaveAfterDelayAsync(snapshot, token);
    }

    private async Task SaveAfterDelayAsync(IReadOnlyList<RaidPlanEntry> snapshot, CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
            await _planStore.SaveAsync(snapshot, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not save raid plan: {exception.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RaidPlanItemViewModel : RaidIconViewModelBase
{
    private readonly RaidCalculatorEngine _engine;
    private IReadOnlyList<long> _smartSourceIds = [];
    private RaidComparisonMode _smartMode = RaidComparisonMode.LowestSulfur;
    private int _quantity;
    private RaidMethodViewModel? _selectedMethod;
    private RaidMethodViewModel? _smartMethod;

    public RaidPlanItemViewModel(RaidTarget target, RaidCalculatorEngine engine, int quantity, long selectedSourceId, RaidComparisonMode mode)
    {
        Target = target;
        _engine = engine;
        _quantity = Math.Clamp(quantity, 1, 9999);
        RefreshMethods(selectedSourceId, mode);
    }

    public RaidTarget Target { get; }
    public string Category => Target.Category;
    public string HealthText => $"{Target.StartHealth:0.#} HP";
    public ImageSource? Icon => GetPackIcon(RaidTargetVisuals.GetLocalIconUri(Target));
    public ObservableCollection<RaidMethodViewModel> Methods { get; } = [];
    public bool UserSelectedMethod { get; set; }

    public int Quantity
    {
        get => _quantity;
        set
        {
            int valid = Math.Clamp(value, 1, 9999);
            if (_quantity == valid) return;
            long selected = SelectedMethod?.Source.SourceId ?? 0;
            _quantity = valid;
            OnPropertyChanged();
            RefreshMethods(selected, RaidComparisonMode.Custom);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public RaidMethodViewModel? SelectedMethod
    {
        get => _selectedMethod;
        set
        {
            if (_selectedMethod == value) return;
            if (value is not null && value != _smartMethod)
            {
                _smartSourceIds = [];
                if (_smartMethod is not null) Methods.Remove(_smartMethod);
                _smartMethod = null;
            }
            _selectedMethod = value;
            UserSelectedMethod = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MethodWarning));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string MethodWarning => SelectedMethod is { HasCraftCost: false } ? "Crafting cost unavailable in raid-data.json" : string.Empty;

    public void ApplyRecommendation(RaidComparisonMode mode)
    {
        if (_smartSourceIds.Count > 0)
        {
            ApplySmartMix(_smartSourceIds, mode);
            return;
        }
        RaidMethodResult? recommendation = RaidCalculatorEngine.Recommend(
            Methods.Where(method => !method.IsCombination).SelectMany(method => method.Results), mode);
        SetSelected(recommendation?.Source.SourceId ?? SelectedMethod?.Source.SourceId ?? 0);
    }

    public void ApplySmartMix(IReadOnlyList<long> sourceIds, RaidComparisonMode mode)
    {
        _smartSourceIds = sourceIds;
        _smartMode = mode;
        if (_smartMethod is not null) Methods.Remove(_smartMethod);
        _smartMethod = null;
        if (sourceIds.Count == 0)
        {
            SetSelected(SelectedMethod?.SingleSourceId ?? Methods.FirstOrDefault()?.SingleSourceId ?? 0);
            return;
        }

        IReadOnlyList<RaidMethodResult> results = _engine.GetBestCombination(Target, sourceIds, mode, Quantity);
        if (results.Count == 0)
        {
            SetSelected(Methods.FirstOrDefault()?.SingleSourceId ?? 0);
            return;
        }
        var mix = new RaidMethodViewModel(results);
        _smartMethod = mix;
        Methods.Insert(0, mix);
        _selectedMethod = mix;
        UserSelectedMethod = false;
        OnPropertyChanged(nameof(SelectedMethod));
        OnPropertyChanged(nameof(MethodWarning));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool SelectSource(long sourceId)
    {
        RaidMethodViewModel? method = Methods.FirstOrDefault(candidate => candidate.SingleSourceId == sourceId);
        if (method is null) return false;
        _selectedMethod = method;
        UserSelectedMethod = false;
        OnPropertyChanged(nameof(SelectedMethod));
        OnPropertyChanged(nameof(MethodWarning));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RefreshMethods(long selectedSourceId, RaidComparisonMode mode)
    {
        Methods.Clear();
        foreach (RaidMethodResult result in _engine.GetMethods(Target, Quantity)) Methods.Add(new RaidMethodViewModel(result));
        if (_smartSourceIds.Count > 0)
        {
            ApplySmartMix(_smartSourceIds, _smartMode);
            return;
        }
        RaidMethodResult? recommendation = RaidCalculatorEngine.Recommend(Methods.SelectMany(method => method.Results), mode);
        SetSelected(selectedSourceId != 0 ? selectedSourceId : recommendation?.Source.SourceId ?? Methods.FirstOrDefault()?.Source.SourceId ?? 0);
    }

    private void SetSelected(long sourceId)
    {
        _selectedMethod = Methods.FirstOrDefault(method => method.SingleSourceId == sourceId) ?? Methods.FirstOrDefault();
        UserSelectedMethod = false;
        OnPropertyChanged(nameof(SelectedMethod));
        OnPropertyChanged(nameof(MethodWarning));
    }

    public event EventHandler? Changed;
}

public sealed class RaidMethodViewModel : RaidIconViewModelBase
{
    public RaidMethodViewModel(RaidMethodResult result) : this([result]) { }
    public RaidMethodViewModel(IReadOnlyList<RaidMethodResult> results) => Results = results;

    public IReadOnlyList<RaidMethodResult> Results { get; }
    public RaidSource Source => Results[0].Source;
    public long SingleSourceId => Results.Count == 1 ? Source.SourceId : 0;
    public bool IsCombination => Results.Count > 1;
    public ImageSource? Icon => GetIcon(Source.ItemId ?? 0, Source.ItemShortname, 24);
    public int RequiredItems => Results.Sum(result => result.RequiredItems);
    public bool HasCraftCost => Results.All(result => result.HasCraftCost);
    public string DisplayName => IsCombination ? "Smart mix" : Source.DisplayName;
    public string DisplayText => string.Join(" + ", Results.Select(result => $"{result.Source.DisplayName} ×{result.RequiredItems}"));
    public string CostText
    {
        get
        {
            if (!HasCraftCost) return "Cost unavailable";
            IReadOnlyList<RaidResourceTotal> resources = RaidCalculatorEngine.Aggregate(Results);
            return resources.Count == 0 ? "No crafting ingredients" :
                string.Join(" · ", resources.Select(resource => $"{resource.DisplayName} {resource.Amount:0.#}"));
        }
    }
    public string DamageText => Results.Count == 0 ? "Damage unavailable" :
        $"{Results.Sum(result => result.TotalDamage):0.#} total damage · {Results.Sum(result => result.Overkill):0.#} overkill";
}

public sealed class RaidTargetCardViewModel : RaidIconViewModelBase
{
    public RaidTargetCardViewModel(RaidTarget target) => Target = target;

    public RaidTarget Target { get; }
    public string Category => Target.Category;
    public string HealthText => $"{Target.StartHealth:0.#} HP";
    public string CardTitle => string.IsNullOrWhiteSpace(Target.BuildingTier)
        ? Target.DisplayName
        : Target.DisplayName.Replace($" ({Target.BuildingTier})", string.Empty, StringComparison.Ordinal);
    public string CardMeta => Target.BuildingTier switch
    {
        "TopTier" => $"Armored · {HealthText}",
        "Twigs" => $"Twig · {HealthText}",
        { Length: > 0 } tier => $"{tier} · {HealthText}",
        _ => HealthText
    };
    public string SearchText => $"{Target.DisplayName} {Target.ComponentType} {Target.BuildingTier} {Target.ItemCategorySlug}";
    public ImageSource? Icon => GetPackIcon(RaidTargetVisuals.GetLocalIconUri(Target));
}

internal static class RaidTargetVisuals
{
    public static string? GetLocalIconUri(RaidTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.ItemShortname))
            return $"pack://application:,,,/Assets/icons/raid-targets/{target.ItemShortname}.png";

        string? tier = GetTierSlug(target.BuildingTier);
        return tier is null || string.IsNullOrWhiteSpace(target.BuildingSlug)
            ? null
            : $"pack://application:,,,/Assets/icons/raid-targets/{tier}-{target.BuildingSlug}.webp";
    }

    private static string? GetTierSlug(string? buildingTier) => buildingTier switch
    {
        "Twigs" => "twig",
        "Wood" => "wood",
        "Stone" => "stone",
        "Metal" => "metal",
        "TopTier" => "armored",
        _ => null
    };
}

public sealed class RaidResourceTotalViewModel(RaidResourceTotal resource) : RaidIconViewModelBase
{
    public string Shortname => resource.Shortname;
    public string DisplayName => resource.DisplayName;
    public double Amount => resource.Amount;
    public string AmountText => resource.Amount.ToString("0.#");
    public ImageSource? Icon => GetIcon(resource.ItemId, resource.Shortname, 28);
}

public sealed class RaidItemTotalViewModel(RaidItemTotal item) : RaidIconViewModelBase
{
    public string DisplayName => item.Source.DisplayName;
    public string AmountText => $"×{item.Amount}";
    public ImageSource? Icon => GetIcon(item.Source.ItemId ?? 0, item.Source.ItemShortname, 28);
}

public abstract class RaidIconViewModelBase : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _isLoadingIcon;

    protected ImageSource? GetIcon(int itemId, string? shortname, int size)
        => GetIcon(() => MainWindow.ResolveItemIcon(itemId, shortname, size));

    protected ImageSource? GetPackIcon(string? uri)
        => GetIcon(() => string.IsNullOrWhiteSpace(uri) ? null : new BitmapImage(new Uri(uri)));

    private ImageSource? GetIcon(Func<ImageSource?> resolve)
    {
        if (_icon is not null || _isLoadingIcon) return _icon;
        _icon = resolve();
        if (_icon is null)
        {
            _isLoadingIcon = true;
            _ = RefreshIconAsync(resolve);
        }
        return _icon;
    }

    private async Task RefreshIconAsync(Func<ImageSource?> resolve)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(250 + (attempt * 150));
            ImageSource? icon = resolve();
            if (icon is null) continue;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _icon = icon;
                _isLoadingIcon = false;
                OnPropertyChanged("Icon");
            });
            return;
        }
        _isLoadingIcon = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record RaidComparisonChoice(RaidComparisonMode Mode, string DisplayName);

public sealed class RaidGlobalMethodChoice : RaidIconViewModelBase
{
    private bool _isSelected;
    public RaidGlobalMethodChoice(RaidSource? source) => Source = source;

    public RaidSource? Source { get; }
    public bool IsSelectable => Source is not null;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }
    public string DisplayName => Source?.ItemShortname switch
    {
        "explosive.timed" => "C4 (Timed Explosive Charge)",
        "ammo.rocket.hv" => "HV Rocket",
        _ => Source?.DisplayName ?? "Auto / per target"
    };
    public ImageSource? Icon => Source is null ? null : GetIcon(Source.ItemId ?? 0, Source.ItemShortname, 22);
}
