using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;
using RustPlusDesk.Services;

using TextBlock = System.Windows.Controls.TextBlock;
using Button = System.Windows.Controls.Button;

namespace RustPlusDesk.Views
{
    public partial class ShopSearchControl : UserControl
    {
        private MainWindow? _mainWindow;
        private AutocompleteItem? _selectedItem;
        private System.Threading.CancellationTokenSource? _searchCts;

        private bool _filterSell = true;
        private bool _filterBuy = true;
        private bool _filterHideZero = false;
        private bool _filterHideMonuments = false;

        private string _sortMode = "default";
        private int _resourceFilterId = 0;
        private string _selectedCategory = "all";

        private static readonly HashSet<string> KnownMonumentShopLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "Resource Exchange", "Outpost Resource", "Extra Resource", "Medical Shop",
            "Building Shop", "Components Shop", "Weapons Shop", "Attire shop",
            "Farming shop", "Main Food Shop", "Outpost",
            "Bandit Arms Extra", "Bandit Arms Dealer", "Bandit Produce", "Bandit Camp Black Market",
            "Produce Shop", "Camp Shop", "Casino Bar Shopkeeper", "Bandit Weapons Shop", "Bandit Town", "Bandit Camp",
            "Boat Vendor", "Fishing shop", "Boat Shop", "Fishing Village Shop", "Fishing Village",
            "Horse Vendor", "Stable Vendor", "Ranch Shop", "Barn Shop", "Ranch", "Stables",
            "Ferry Terminal", "Cargo Ship"
        };

        private bool IsMonumentShop(RustPlusClientReal.ShopMarker s)
        {
            // 1. Off-map / Deep Sea NPC shops
            if (s.X < 0 || s.Y < 0) return true;

            // 2. Check label against known NPC / Monument Shop names & keywords
            if (!string.IsNullOrWhiteSpace(s.Label))
            {
                string label = s.Label.Trim();
                if (KnownMonumentShopLabels.Contains(label)) return true;

                string lower = label.ToLowerInvariant();
                if (lower.Contains("outpost") || lower.Contains("bandit") || lower.Contains("fishing") || 
                    lower.Contains("ranch") || lower.Contains("stable") || lower.Contains("ferry") || 
                    lower.Contains("cargo") || lower.Contains("monument"))
                {
                    return true;
                }
            }

            // 3. Proximity to map monuments
            if (_mainWindow != null)
            {
                var mons = _mainWindow.GetMapMonumentsList();
                if (mons != null && mons.Any())
                {
                    foreach (var m in mons)
                    {
                        if (string.IsNullOrWhiteSpace(m.Name)) continue;
                        string mLower = m.Name.ToLowerInvariant();

                        bool isTargetMonument = mLower.Contains("outpost") || mLower.Contains("bandit") || 
                                                 mLower.Contains("fishing") || mLower.Contains("ranch") || 
                                                 mLower.Contains("stable") || mLower.Contains("barn") || 
                                                 mLower.Contains("ferry") || mLower.Contains("village") ||
                                                 mLower.Contains("compound");

                        if (isTargetMonument)
                        {
                            double dx = m.X - s.X;
                            double dy = m.Y - s.Y;
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            if (dist <= 180.0) // 180m radius covers the monument perimeter
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        // Custom colors matching HTML premium styles
        private static readonly Brush ActiveAccentBg = new SolidColorBrush(Color.FromArgb(38, 0, 173, 239)); // rgba(0, 173, 239, .15)
        private static readonly Brush ActiveAccentBorder = new SolidColorBrush(Color.FromRgb(0, 173, 239)); // #00adef
        private static readonly Brush ActiveAccentText = new SolidColorBrush(Color.FromRgb(0, 173, 239));

        private static readonly Brush ActiveRedBg = new SolidColorBrush(Color.FromArgb(26, 239, 83, 80)); // rgba(239, 83, 80, .1)
        private static readonly Brush ActiveRedBorder = new SolidColorBrush(Color.FromRgb(239, 83, 80)); // #ef5350
        private static readonly Brush ActiveRedText = new SolidColorBrush(Color.FromRgb(239, 83, 80));

        private static readonly Brush InactiveBg = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)); // rgba(255, 255, 255, .05)
        private static readonly Brush InactiveBorder = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)); // rgba(255, 255, 255, .08)
        private static readonly Brush InactiveText = new SolidColorBrush(Color.FromArgb(153, 236, 239, 241)); // rgba(236, 239, 241, .6)

        // Category loading and mapping cache
        private static bool _categoriesLoaded = false;
        private static readonly Dictionary<string, string> _shortNameToCategory = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _itemIdToCategory = new();
        private static readonly List<string> _allCategoriesList = new();

        private static readonly Dictionary<string, string> _categoryRepresentativeIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Weapons"] = "rifle.ak",
            ["Ammunition"] = "ammo.rifle",
            ["Components"] = "riflebody",
            ["Attire"] = "metal.facemask",
            ["Electrical"] = "autoturret",
            ["Medical"] = "syringe.medical",
            ["Food"] = "pumpkin",
            ["Tool"] = "hatchet",
            ["Tools"] = "hatchet",
            ["Construction"] = "building.planner",
            ["Fun"] = "fun.guitar",
            ["Items"] = "box.wooden.large",
            ["Misc"] = "keycard_red",
            ["Resources"] = "scrap",
            ["Traps"] = "guntrap",
            ["Vehicle"] = "minicopter",
            ["Vehicles"] = "minicopter"
        };

        private static ImageSource? GetCategoryIcon(string categoryName)
        {
            if (_categoryRepresentativeIcons.TryGetValue(categoryName, out var shortName))
            {
                var icon = MainWindow.ResolveItemIcon(0, shortName, 32);
                if (icon != null) return icon;
            }

            EnsureCategoriesLoaded();
            var firstItemShort = _shortNameToCategory.FirstOrDefault(kv => kv.Value.Equals(categoryName, StringComparison.OrdinalIgnoreCase)).Key;
            if (!string.IsNullOrEmpty(firstItemShort))
            {
                return MainWindow.ResolveItemIcon(0, firstItemShort, 32);
            }

            return null;
        }

        private static void EnsureCategoriesLoaded()
        {
            if (_categoriesLoaded) return;
            _categoriesLoaded = true;

            try
            {
                string jsonContent = "";
                bool loaded = false;

                var baseDir = AppContext.BaseDirectory;
                var currDir = System.IO.Directory.GetCurrentDirectory();
                var entryAsm = System.Reflection.Assembly.GetEntryAssembly();
                var entryDir = entryAsm != null ? System.IO.Path.GetDirectoryName(entryAsm.Location) : null;

                var filePaths = new[]
                {
                    System.IO.Path.Combine(baseDir, "recycler-items.json"),
                    System.IO.Path.Combine(currDir, "recycler-items.json"),
                    entryDir is null ? null : System.IO.Path.Combine(entryDir, "recycler-items.json"),
                    System.IO.Path.Combine(baseDir, "assets", "recycler-items.json"),
                    System.IO.Path.Combine(baseDir, "data",   "recycler-items.json"),
                    System.IO.Path.Combine(baseDir, "Assets", "Data", "recycler-items.json"),
                };

                foreach (var path in filePaths)
                {
                    if (path != null && System.IO.File.Exists(path))
                    {
                        try
                        {
                            jsonContent = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                            loaded = true;
                            break;
                        }
                        catch { }
                    }
                }

                if (!loaded)
                {
                    string asmName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "RustPlusDesk";
                    var packUris = new[]
                    {
                        "pack://application:,,,/recycler-items.json",
                        "pack://application:,,,/assets/recycler-items.json",
                        "pack://application:,,,/data/recycler-items.json",
                        "pack://application:,,,/Assets/Data/recycler-items.json",
                        $"pack://application:,,,/{asmName};component/recycler-items.json",
                        $"pack://application:,,,/{asmName};component/assets/recycler-items.json",
                        $"pack://application:,,,/{asmName};component/data/recycler-items.json",
                        $"pack://application:,,,/{asmName};component/Assets/Data/recycler-items.json",
                    };

                    foreach (var uri in packUris)
                    {
                        try
                        {
                            var sri = Application.GetResourceStream(new Uri(uri));
                            if (sri?.Stream != null)
                            {
                                using var r = new System.IO.StreamReader(sri.Stream);
                                jsonContent = r.ReadToEnd();
                                loaded = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (!loaded)
                {
                    string entryName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "RustPlusDesk";
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    var resName = $"{entryName}.Assets.Data.recycler-items.json";
                    try
                    {
                        using var stream = asm.GetManifestResourceStream(resName);
                        if (stream != null)
                        {
                            using var r = new System.IO.StreamReader(stream);
                            jsonContent = r.ReadToEnd();
                            loaded = true;
                        }
                    }
                    catch { }
                }

                if (loaded && !string.IsNullOrEmpty(jsonContent))
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var parsedItems = System.Text.Json.JsonSerializer.Deserialize<List<RecyclerItemData>>(jsonContent, options);
                    if (parsedItems != null)
                    {
                        var catSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in parsedItems)
                        {
                            if (!string.IsNullOrEmpty(item.category))
                            {
                                catSet.Add(item.category);
                                if (!string.IsNullOrEmpty(item.shortName))
                                {
                                    _shortNameToCategory[item.shortName] = item.category;
                                }
                                if (item.ingameId != 0)
                                {
                                    _itemIdToCategory[item.ingameId] = item.category;
                                }
                                if (int.TryParse(item.id, out int parsedId) && parsedId != 0)
                                {
                                    _itemIdToCategory[parsedId] = item.category;
                                }
                            }
                        }
                        var orderedKeys = _categoryRepresentativeIcons.Keys.ToList();
                        _allCategoriesList.Clear();
                        _allCategoriesList.AddRange(catSet.OrderBy(c =>
                        {
                            int idx = orderedKeys.FindIndex(k => k.Equals(c, StringComparison.OrdinalIgnoreCase));
                            return idx >= 0 ? idx : int.MaxValue;
                        }));
                    }
                }
            }
            catch { }
        }

        public static string? GetItemCategory(int itemId, string? itemShortName)
        {
            EnsureCategoriesLoaded();

            if (itemId != 0 && _itemIdToCategory.TryGetValue(itemId, out var catByItemId))
            {
                return catByItemId;
            }

            if (!string.IsNullOrEmpty(itemShortName) && _shortNameToCategory.TryGetValue(itemShortName, out var catByShort))
            {
                return catByShort;
            }

            if (itemId != 0 && MainWindow.sItemsById.TryGetValue(itemId, out var info) && !string.IsNullOrEmpty(info.ShortName))
            {
                if (_shortNameToCategory.TryGetValue(info.ShortName, out var catByResolvedShort))
                {
                    return catByResolvedShort;
                }
            }

            return null;
        }

        public ShopSearchControl()
        {
            InitializeComponent();
        }

        public void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            
            // Initial button styles
            UpdateFilterButtonsStyles();
            
            // Populate category dropdown with icons
            PopulateCategoryComboBox();

            // Populate resource dropdown with icons
            PopulateResourceComboBox();
            
            // Populate current alerts
            RefreshAlertListUI();

            // Refresh search results
            RefreshSearchResults();
        }

        public class CategoryItem
        {
            public string Name { get; set; } = "all";
            public string Display { get; set; } = "All Categories";
            public ImageSource? Icon { get; set; }
        }

        private void PopulateCategoryComboBox()
        {
            EnsureCategoriesLoaded();

            var categories = new List<CategoryItem>
            {
                new CategoryItem { Name = "all", Display = "All Categories", Icon = null }
            };

            foreach (var cat in _allCategoriesList)
            {
                categories.Add(new CategoryItem
                {
                    Name = cat,
                    Display = cat,
                    Icon = GetCategoryIcon(cat)
                });
            }

            CmbCategory.ItemsSource = categories;
            CmbCategory.SelectedIndex = 0;
        }

        // Helper for Resource ComboBox with icons
        public class ResourceItem
        {
            public int Id { get; set; }
            public string Display { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
            public ImageSource? Icon { get; set; }
        }

        private void PopulateResourceComboBox()
        {
            var resources = new List<ResourceItem>
            {
                new ResourceItem { Id = 0, Display = "All Resources", Tag = "all", Icon = null },
                new ResourceItem { Id = -932201673, Display = "Scrap", Tag = "scrap", Icon = MainWindow.ResolveItemIcon(-932201673, "scrap") },
                new ResourceItem { Id = 317398316, Display = "High Quality Metal", Tag = "hqm", Icon = MainWindow.ResolveItemIcon(317398316, "metal.refined") },
                new ResourceItem { Id = -1581843485, Display = "Sulfur", Tag = "sulfur", Icon = MainWindow.ResolveItemIcon(-1581843485, "sulfur") },
                new ResourceItem { Id = 69511070, Display = "Metal Fragments", Tag = "metal", Icon = MainWindow.ResolveItemIcon(69511070, "metal.fragments") },
                new ResourceItem { Id = -2099697608, Display = "Stone", Tag = "stone", Icon = MainWindow.ResolveItemIcon(-2099697608, "stones") },
                new ResourceItem { Id = -151838493, Display = "Wood", Tag = "wood", Icon = MainWindow.ResolveItemIcon(-151838493, "wood") },
                new ResourceItem { Id = -4031221, Display = "Metal Ore", Tag = "metalore", Icon = MainWindow.ResolveItemIcon(-4031221, "metal.ore") },
                new ResourceItem { Id = -1157596551, Display = "Sulfur Ore", Tag = "sulfurore", Icon = MainWindow.ResolveItemIcon(-1157596551, "sulfur.ore") },
                new ResourceItem { Id = -1982036270, Display = "High Quality Ore", Tag = "hqore", Icon = MainWindow.ResolveItemIcon(-1982036270, "hq.metal.ore") },
                new ResourceItem { Id = -321733511, Display = "Crude Oil", Tag = "crude", Icon = MainWindow.ResolveItemIcon(-321733511, "crude.oil") },
                new ResourceItem { Id = -946369541, Display = "Low Grade", Tag = "lowgrade", Icon = MainWindow.ResolveItemIcon(-946369541, "lowgradefuel") },
                new ResourceItem { Id = 1568388703, Display = "Diesel", Tag = "diesel", Icon = MainWindow.ResolveItemIcon(1568388703, "diesel_barrel") },
                new ResourceItem { Id = -858312878, Display = "Cloth", Tag = "cloth", Icon = MainWindow.ResolveItemIcon(-858312878, "cloth") },
                new ResourceItem { Id = -1938052175, Display = "Charcoal", Tag = "charcoal", Icon = MainWindow.ResolveItemIcon(-1938052175, "charcoal") },
            };
            CmbResource.ItemsSource = resources;
            CmbResource.SelectedIndex = 0;
        }

        // Autocomplete item helper
        public class AutocompleteItem
        {
            public int Id { get; set; }
            public string Display { get; set; } = string.Empty;
            public string ShortName { get; set; } = string.Empty;
            public ImageSource? Icon { get; set; }
            public bool IsCategory { get; set; } = false;
            public string CategoryName { get; set; } = string.Empty;

            public Visibility CategoryBadgeVisibility => IsCategory ? Visibility.Visible : Visibility.Collapsed;
        }

        // Autocomplete filtering logic
        private void TxtShopSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_mainWindow == null) return;
            
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            var token = _searchCts.Token;

            string query = TxtShopSearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                _selectedCategory = "all";
                if (CmbCategory != null && CmbCategory.SelectedIndex != 0)
                {
                    CmbCategory.SelectedIndex = 0;
                }
                PopupAutocomplete.IsOpen = false;
                _selectedItem = null;
                RefreshSearchResults();
                return;
            }

            if (_selectedItem != null && _selectedItem.Display == TxtShopSearch.Text)
            {
                // Selected item matches, don't reopen dropdown
                return;
            }

            // Debounce delay of 200ms
            var dispatcher = Dispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(200, token);
                    if (token.IsCancellationRequested) return;

                    await RunAutocompleteAndSearchAsync(query, token);
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    // Swallowed
                }
            });
        }

        private async System.Threading.Tasks.Task RunAutocompleteAndSearchAsync(string query, System.Threading.CancellationToken token)
        {
            var lowercaseQuery = query.ToLowerInvariant();
            EnsureCategoriesLoaded();
            
            // Search both categories and items db on a background thread to prevent UI thread lock
            var matches = await System.Threading.Tasks.Task.Run(() =>
            {
                var result = new List<AutocompleteItem>();

                // 1. Categories matching query (or all categories if query is empty)
                var matchedCategories = string.IsNullOrWhiteSpace(lowercaseQuery)
                    ? _allCategoriesList
                    : _allCategoriesList.Where(c => c.Contains(lowercaseQuery, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var cat in matchedCategories)
                {
                    result.Add(new AutocompleteItem
                    {
                        Id = 0,
                        Display = cat,
                        ShortName = cat.ToLowerInvariant(),
                        Icon = GetCategoryIcon(cat),
                        IsCategory = true,
                        CategoryName = cat
                    });
                }

                // 2. Items matching query
                if (!string.IsNullOrWhiteSpace(lowercaseQuery))
                {
                    var itemMatches = MainWindow.sItemsById.Values
                        .Where(ii => !string.IsNullOrWhiteSpace(ii.Display) && 
                                     (ii.Display.Contains(lowercaseQuery, StringComparison.OrdinalIgnoreCase) || 
                                      (ii.ShortName != null && ii.ShortName.Contains(lowercaseQuery, StringComparison.OrdinalIgnoreCase))))
                        .Take(20)
                        .Select(ii => new AutocompleteItem
                        {
                            Id = ii.Id,
                            Display = ii.Display,
                            ShortName = ii.ShortName ?? "",
                            Icon = MainWindow.ResolveItemIcon(ii.Id, ii.ShortName, 32),
                            IsCategory = false,
                            CategoryName = string.Empty
                        })
                        .ToList();

                    result.AddRange(itemMatches);
                }

                return result;
            });

            if (token.IsCancellationRequested) return;

            if (matches.Any())
            {
                LstAutocomplete.ItemsSource = matches;
                PopupAutocomplete.IsOpen = true;
            }
            else
            {
                PopupAutocomplete.IsOpen = false;
            }

            _selectedItem = null; // Reset exact match when typing freely
            await RefreshSearchResultsAsync(lowercaseQuery, token);
        }

        private void LstAutocomplete_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            if (LstAutocomplete.SelectedItem is AutocompleteItem selected)
            {
                _selectedItem = selected;
                TxtShopSearch.TextChanged -= TxtShopSearch_TextChanged;

                if (selected.IsCategory)
                {
                    _selectedCategory = selected.CategoryName;
                    TxtShopSearch.Text = selected.CategoryName;

                    if (CmbCategory != null)
                    {
                        var catItem = CmbCategory.Items.OfType<CategoryItem>().FirstOrDefault(c => c.Name.Equals(selected.CategoryName, StringComparison.OrdinalIgnoreCase));
                        if (catItem != null)
                        {
                            CmbCategory.SelectedItem = catItem;
                        }
                    }
                }
                else
                {
                    TxtShopSearch.Text = selected.Display;
                }

                TxtShopSearch.TextChanged += TxtShopSearch_TextChanged;
                
                PopupAutocomplete.IsOpen = false;
                RefreshSearchResults();
            }
        }

        // Autocomplete Suggestion keyboard navigation
        private void TxtShopSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (PopupAutocomplete.IsOpen && LstAutocomplete.Items.Count > 0)
            {
                int index = LstAutocomplete.SelectedIndex;
                if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    index = (index + 1) % LstAutocomplete.Items.Count;
                    LstAutocomplete.SelectedIndex = index;
                    LstAutocomplete.ScrollIntoView(LstAutocomplete.SelectedItem);
                }
                else if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    index = index <= 0 ? LstAutocomplete.Items.Count - 1 : index - 1;
                    LstAutocomplete.SelectedIndex = index;
                    LstAutocomplete.ScrollIntoView(LstAutocomplete.SelectedItem);
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    if (LstAutocomplete.SelectedItem != null)
                    {
                        LstAutocomplete_SelectionChanged(null, null);
                    }
                    else
                    {
                        PopupAutocomplete.IsOpen = false;
                        RefreshSearchResults();
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    PopupAutocomplete.IsOpen = false;
                }
            }
            else if (e.Key == Key.Enter)
            {
                RefreshSearchResults();
            }
        }

        private void TxtShopSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            var token = _searchCts.Token;

            string query = TxtShopSearch.Text.Trim();
            _ = RunAutocompleteAndSearchAsync(query, token);
        }

        private void TxtShopSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            // Delay to allow suggestion mouse clicks to register before closing popup
            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(200);
                if (!TxtShopSearch.IsFocused && !LstAutocomplete.IsFocused && !PopupAutocomplete.IsFocused)
                {
                    PopupAutocomplete.IsOpen = false;
                }
            });
        }

        // Filter button color states
        public void UpdateFilterButtonsStyles()
        {
            ApplyToggleButtonStyle(TglShopSells, _filterSell, false);
            ApplyToggleButtonStyle(TglShopBuys, _filterBuy, false);
            ApplyToggleButtonStyle(TglShopHideZero, _filterHideZero, false);
            if (TglShopHideMonuments != null)
            {
                ApplyToggleButtonStyle(TglShopHideMonuments, _filterHideMonuments, true);
            }
        }

        private void ApplyToggleButtonStyle(Button btn, bool isOn, bool isRed)
        {
            if (isOn)
            {
                btn.Background = isRed ? ActiveRedBg : ActiveAccentBg;
                btn.BorderBrush = isRed ? ActiveRedBorder : ActiveAccentBorder;
                btn.Foreground = isRed ? ActiveRedText : ActiveAccentText;
            }
            else
            {
                btn.Background = InactiveBg;
                btn.BorderBrush = InactiveBorder;
                btn.Foreground = InactiveText;
            }
        }

        // Action Tool Buttons clicks
        private void BtnShopProfit_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null) return;
            _mainWindow.OpenAnalysisWindow();
        }

        private void BtnShopBuyX_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null) return;
            _mainWindow.OpenPathFinderWindow();
        }

        private void BtnShopAddAlert_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow == null) return;
            string query = TxtShopSearch.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;

            // Expand Alerts panel
            BrdAlertListContainer.Visibility = Visibility.Visible;
            TxtAlertArrow.Text = "▲";

            var rule = new MainWindow.ShopAlertRule
            {
                QueryText = query,
                MatchSellSide = _filterSell,
                MatchBuySide = _filterBuy,
                NotifyChat = true,
                NotifySound = true
            };

            // Baseline rules
            var lastShops = _mainWindow.GetLastShopsList();
            foreach (var shop in lastShops)
            {
                if (shop.Orders == null) continue;
                foreach (var o in shop.Orders)
                {
                    rule.Baseline.Add(new MainWindow.AlertSeenOrder
                    {
                        ShopId = shop.Id,
                        ItemShort = o.ItemShortName ?? "",
                        CurrencyShort = o.CurrencyShortName ?? "",
                        Quantity = o.Quantity,
                        CurrencyAmount = o.CurrencyAmount,
                        Stock = o.Stock
                    });
                }
            }

            _mainWindow.AddAlertRule(rule);
            
            RefreshAlertListUI();
            
            // Visual alert flash effect
            var prevBg = BtnShopAddAlert.Background;
            BtnShopAddAlert.Background = ActiveAccentBg;
            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(300);
                BtnShopAddAlert.Background = prevBg;
            });
        }

        // Toggle buttons actions
        private void TglShopSells_Click(object sender, RoutedEventArgs e)
        {
            _filterSell = !_filterSell;
            UpdateFilterButtonsStyles();
            RefreshSearchResults();
        }

        private void TglShopBuys_Click(object sender, RoutedEventArgs e)
        {
            _filterBuy = !_filterBuy;
            UpdateFilterButtonsStyles();
            RefreshSearchResults();
        }

        private void TglShopHideZero_Click(object sender, RoutedEventArgs e)
        {
            _filterHideZero = !_filterHideZero;
            UpdateFilterButtonsStyles();
            RefreshSearchResults();
        }

        private void TglShopHideMonuments_Click(object sender, RoutedEventArgs e)
        {
            _filterHideMonuments = !_filterHideMonuments;
            UpdateFilterButtonsStyles();
            RefreshSearchResults();
        }

        private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSort.SelectedItem is ComboBoxItem item)
            {
                _sortMode = item.Tag?.ToString() ?? "default";
                RefreshSearchResults();
            }
        }

        private void CmbResource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbResource.SelectedItem is ResourceItem item)
            {
                _resourceFilterId = item.Id;
                RefreshSearchResults();
            }
        }

        private void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCategory.SelectedItem is CategoryItem item)
            {
                _selectedCategory = item.Name;
                RefreshSearchResults();
            }
        }

        // Collapsible alerts section trigger
        private void AlertHdr_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (BrdAlertListContainer == null) return;
            if (BrdAlertListContainer.Visibility == Visibility.Collapsed)
            {
                BrdAlertListContainer.Visibility = Visibility.Visible;
                TxtAlertArrow.Text = "▲";
            }
            else
            {
                BrdAlertListContainer.Visibility = Visibility.Collapsed;
                TxtAlertArrow.Text = "▼";
            }
        }

        // Dynamically populates matching shops list
        public void RefreshSearchResults()
        {
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            string query = TxtShopSearch.Text.Trim().ToLowerInvariant();
            _ = RefreshSearchResultsAsync(query, _searchCts.Token);
        }

        public async System.Threading.Tasks.Task RefreshSearchResultsAsync(string query, System.Threading.CancellationToken token)
        {
            if (_mainWindow == null || ShopSearchResultsContainer == null) return;

            var shops = _mainWindow.GetLastShopsList();
            if (shops == null || !shops.Any())
            {
                ShopSearchResultsContainer.Children.Clear();
                ShowEmptyMessage(string.IsNullOrWhiteSpace(query) ? "No shops loaded yet..." : "No matching shops found.");
                return;
            }

            // Perform CPU-heavy shop list filtering on background thread
            var matchedResults = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<(RustPlusClientReal.ShopMarker Shop, List<RustPlusClientReal.ShopOrder> Offers)>();
                
                EnsureCategoriesLoaded();
                bool hasResourceFilter = _resourceFilterId != 0;
                bool hasCategoryFilter = !string.IsNullOrEmpty(_selectedCategory) && !_selectedCategory.Equals("all", StringComparison.OrdinalIgnoreCase);

                string? queryCategoryMatch = null;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    queryCategoryMatch = _allCategoriesList.FirstOrDefault(c => c.Equals(query, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var s in shops)
                {
                    if (s.Orders == null || !s.Orders.Any()) continue;

                    // Filter out monument shops if toggle is active
                    if (_filterHideMonuments && IsMonumentShop(s)) continue;

                    bool hasQuery = !string.IsNullOrWhiteSpace(query);

                    var shopMatches = s.Orders.Where(o =>
                    {
                        // 1. Stock filter
                        if (_filterHideZero && o.Stock <= 0) return false;

                        // 2. Resource ID filter & Text Query & Category combination
                        string oName = MainWindow.ResolveItemName(o.ItemId, o.ItemShortName).ToLowerInvariant();
                        string cName = MainWindow.ResolveItemName(o.CurrencyItemId, o.CurrencyShortName).ToLowerInvariant();
                        string? oCat = GetItemCategory(o.ItemId, o.ItemShortName);
                        string? cCat = GetItemCategory(o.CurrencyItemId, o.CurrencyShortName);

                        // Combine text query, category filter, and resource filter under the Sells and Buys toggles
                        bool isSellMatch = true;
                        if (hasCategoryFilter)
                        {
                            if (oCat == null || !oCat.Equals(_selectedCategory, StringComparison.OrdinalIgnoreCase))
                                isSellMatch = false;
                        }
                        if (isSellMatch && hasQuery)
                        {
                            bool matchesSellQuery = oName.Contains(query) || 
                                                   (o.ItemShortName != null && o.ItemShortName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                                                   (queryCategoryMatch != null && oCat != null && oCat.Equals(queryCategoryMatch, StringComparison.OrdinalIgnoreCase));
                            if (!matchesSellQuery)
                            {
                                isSellMatch = false;
                            }
                        }
                        if (isSellMatch && hasResourceFilter && o.CurrencyItemId != _resourceFilterId)
                        {
                            isSellMatch = false;
                        }

                        bool isBuyMatch = true;
                        if (hasCategoryFilter)
                        {
                            if (cCat == null || !cCat.Equals(_selectedCategory, StringComparison.OrdinalIgnoreCase))
                                isBuyMatch = false;
                        }
                        if (isBuyMatch && hasQuery)
                        {
                            bool matchesBuyQuery = cName.Contains(query) || 
                                                  (o.CurrencyShortName != null && o.CurrencyShortName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                                                  (queryCategoryMatch != null && cCat != null && cCat.Equals(queryCategoryMatch, StringComparison.OrdinalIgnoreCase));
                            if (!matchesBuyQuery)
                            {
                                isBuyMatch = false;
                            }
                        }
                        if (isBuyMatch && hasResourceFilter && o.ItemId != _resourceFilterId)
                        {
                            isBuyMatch = false;
                        }

                        // Combine with the active Sells and Buys toggles
                        bool matchSell = _filterSell && isSellMatch;
                        bool matchBuy = _filterBuy && isBuyMatch;

                        return matchSell || matchBuy;
                    }).ToList();

                    if (shopMatches.Any())
                    {
                        list.Add((s, shopMatches));
                    }
                }

                // If a resource filter is active and sort is default, auto-switch to price_asc for better UX
                if (_resourceFilterId != 0 && _sortMode == "default")
                {
                    _sortMode = "price_asc";
                    // Update UI state on main thread
                    Dispatcher.Invoke(() => CmbSort.SelectedIndex = 1);
                }

                // Sorting logic
                if (_sortMode != "default")
                {
                    list = list.OrderBy(item =>
                    {
                        if (_sortMode == "stock")
                            return -item.Offers.Max(o => o.Stock);

                        if (_sortMode == "price_asc" || _sortMode == "price_desc")
                        {
                            double bestPrice = _sortMode == "price_asc" ? double.MaxValue : double.MinValue;
                            foreach (var o in item.Offers)
                            {
                                double price = (double)o.CurrencyAmount / Math.Max(1, o.Quantity);
                                
                                // Optimization: If we have a resource filter, we prioritize offers using that currency
                                // to ensure the price comparison is meaningful.
                                if (_resourceFilterId != 0 && o.CurrencyItemId != _resourceFilterId)
                                {
                                    // If we are looking for sulfur, and this offer is for scrap, it's less relevant
                                    // but currently the filter already hides non-sulfur offers if _resourceFilterId is set.
                                }

                                if (_sortMode == "price_asc")
                                    bestPrice = Math.Min(bestPrice, price);
                                else
                                    bestPrice = Math.Max(bestPrice, price);
                            }
                            return _sortMode == "price_asc" ? bestPrice : -bestPrice;
                        }
                        
                        if (_sortMode == "currency")
                        {
                            return item.Offers.FirstOrDefault()?.CurrencyItemId ?? 0;
                        }
                        
                        return 0.0;
                    }).ToList();
                }

                return list;
            });

            if (token.IsCancellationRequested) return;

            // Clear container and dynamically build WPF visual cards on UI thread
            ShopSearchResultsContainer.Children.Clear();

            if (!matchedResults.Any())
            {
                ShowEmptyMessage(string.IsNullOrWhiteSpace(query) ? "No matching shops found." : $"No shops match \"{TxtShopSearch.Text}\"");
                return;
            }

            // Yield control back to Dispatcher periodically if there are many cards
            int count = 0;
            foreach (var item in matchedResults)
            {
                if (token.IsCancellationRequested) return;

                var card = _mainWindow.BuildShopSearchCard(item.Shop, item.Offers, compact: true);
                ShopSearchResultsContainer.Children.Add(card);

                // Yield to keep UI fluid if rendering many elements
                count++;
                if (count % 8 == 0)
                {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
        }

        private void ShowEmptyMessage(string message)
        {
            ShopSearchResultsContainer.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = InactiveText,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            });
        }

        // Custom programmatic Alert rules list builder
        public void RefreshAlertListUI()
        {
            if (_mainWindow == null || LstItemAlerts == null) return;

            LstItemAlerts.Items.Clear();

            var rules = _mainWindow.GetAlertRulesList();
            if (rules == null || !rules.Any())
            {
                LstItemAlerts.Items.Add(new TextBlock
                {
                    Text = "No alerts configured yet.",
                    Foreground = InactiveText,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Padding = new Thickness(6, 4, 6, 4)
                });
                return;
            }

            foreach (var rule in rules.ToList())
            {
                // Sleek translucent card container
                var cardBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Query + Mode
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Chat button
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Sound button
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Save button
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Del button

                // Query text + stylized badge
                var modeStr = (rule.MatchSellSide && rule.MatchBuySide) ? "sell+buy" : rule.MatchSellSide ? "sell" : "buy";
                var textStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                textStack.Children.Add(new TextBlock
                {
                    Text = rule.QueryText,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 120,
                    ToolTip = rule.QueryText
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = $"  {modeStr}",
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 181, 246)), // Soft bright blue badge
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                });
                Grid.SetColumn(textStack, 0);
                grid.Children.Add(textStack);

                // 💬 Chat toggle button
                var btnChat = new Button
                {
                    Content = "💬",
                    Style = (Style)FindResource("AlertActionButton"),
                    ToolTip = "Send to team chat"
                };
                ApplyAlertBtnStyle(btnChat, rule.NotifyChat, false);
                btnChat.Click += (s, ev) =>
                {
                    rule.NotifyChat = !rule.NotifyChat;
                    _mainWindow.SavePersistentAlerts();
                    ApplyAlertBtnStyle(btnChat, rule.NotifyChat, false);
                    _mainWindow.UpdateShopSearchConfig();
                };
                Grid.SetColumn(btnChat, 1);
                grid.Children.Add(btnChat);

                // 🔊 Sound alert toggle button
                var btnSound = new Button
                {
                    Content = "🔊",
                    Style = (Style)FindResource("AlertActionButton"),
                    ToolTip = "Play sound alert"
                };
                ApplyAlertBtnStyle(btnSound, rule.NotifySound, false);
                btnSound.Click += (s, ev) =>
                {
                    rule.NotifySound = !rule.NotifySound;
                    _mainWindow.SavePersistentAlerts();
                    ApplyAlertBtnStyle(btnSound, rule.NotifySound, false);
                };
                Grid.SetColumn(btnSound, 2);
                grid.Children.Add(btnSound);

                // 💾 Persistent save toggle button
                var btnSave = new Button
                {
                    Content = "💾",
                    Style = (Style)FindResource("AlertActionButton"),
                    ToolTip = rule.IsSaved ? "Saved (click to unsave)" : "Save alert"
                };
                ApplyAlertBtnStyle(btnSave, rule.IsSaved, true);
                btnSave.Click += (s, ev) =>
                {
                    rule.IsSaved = !rule.IsSaved;
                    _mainWindow.SavePersistentAlerts();
                    ApplyAlertBtnStyle(btnSave, rule.IsSaved, true);
                    btnSave.ToolTip = rule.IsSaved ? RustPlusDesk.Properties.Resources.GetString("CodeUiSavedClickToUnsave") : RustPlusDesk.Properties.Resources.GetString("CodeUiSaveAlert");
                };
                Grid.SetColumn(btnSave, 3);
                grid.Children.Add(btnSave);

                // 🗑 Delete alert button
                var btnDel = new Button
                {
                    Content = "🗑",
                    Style = (Style)FindResource("AlertActionButton"),
                    Foreground = new SolidColorBrush(Color.FromRgb(239, 83, 80)),
                    ToolTip = "Remove Alert"
                };
                btnDel.Click += (s, ev) =>
                {
                    _mainWindow.RemoveAlertRule(rule);
                    RefreshAlertListUI();
                };
                Grid.SetColumn(btnDel, 4);
                grid.Children.Add(btnDel);

                cardBorder.Child = grid;
                LstItemAlerts.Items.Add(cardBorder);
            }
        }

        private void ApplyAlertBtnStyle(Button btn, bool isOn, bool isGreen)
        {
            if (isOn)
            {
                btn.Background = isGreen 
                    ? new SolidColorBrush(Color.FromArgb(30, 102, 187, 106)) // rgba(102, 187, 106, .12)
                    : ActiveAccentBg;
                btn.BorderBrush = isGreen 
                    ? new SolidColorBrush(Color.FromRgb(102, 187, 106)) 
                    : ActiveAccentBorder;
                btn.Foreground = isGreen 
                    ? new SolidColorBrush(Color.FromRgb(102, 187, 106)) 
                    : ActiveAccentText;
            }
            else
            {
                btn.Background = InactiveBg;
                btn.BorderBrush = InactiveBorder;
                btn.Foreground = InactiveText;
            }
        }
    }
}
