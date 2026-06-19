using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RustPlusDesk.Views
{
    public partial class RecyclerOverlay : UserControl
    {
        public event RoutedEventHandler CloseRequested;

        public ObservableCollection<RecyclerItemViewModel> Items { get; } = new();
        public ObservableCollection<RecyclerOutputViewModel> Outputs { get; } = new();

        private List<RecyclerItemViewModel> _allRecyclerItems = new();
        private List<RecyclerItemViewModel> _filteredItems    = new();

        // Incremental loading — render 25 items at a time
        private const int PageSize    = 25;
        private int       _loadedCount = 0;

        public RecyclerOverlay()
        {
            try
            {
                InitializeComponent();
                InputsControl.ItemsSource = Items;
                OutputsControl.ItemsSource = Outputs;

                LoadItems();

                Loaded += RecyclerOverlay_Loaded;
                MainWindow.IconsUpdated += OnIconsUpdated;

                Unloaded += (s, e) => {
                    MainWindow.IconsUpdated -= OnIconsUpdated;
                };
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(@"C:\Users\Jawad\.gemini\antigravity-ide\brain\c4d06e13-9fd0-4c38-9e9e-769d13bce6c7\scratch");
                    File.WriteAllText(@"C:\Users\Jawad\.gemini\antigravity-ide\brain\c4d06e13-9fd0-4c38-9e9e-769d13bce6c7\scratch\crash.txt", ex.ToString());
                }
                catch { }
                throw;
            }
        }

        private void RecyclerOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            LogDiag($"[RecyclerOverlay] Loaded Event Fired.");
            LogDiag($"[RecyclerOverlay] InputsControl Items Count = {InputsControl.Items.Count}, OutputsControl Items Count = {OutputsControl.Items.Count}");
            LogDiag($"[RecyclerOverlay] InputsControl Visibility = {InputsControl.Visibility}, Width = {InputsControl.ActualWidth}, Height = {InputsControl.ActualHeight}");
            LogDiag($"[RecyclerOverlay] Items collection Count = {Items.Count}, Outputs collection Count = {Outputs.Count}");
        }

        // Pretty display names for output resources
        private static readonly Dictionary<string, string> _knownOutputNames = new()
        {
            ["scrap"]             = "Scrap",
            ["metal.refined"]     = "High Quality Metal",
            ["metal.fragments"]   = "Metal Fragments",
            ["cloth"]             = "Cloth",
            ["rope"]              = "Rope",
            ["techparts"]         = "Tech Parts",
            ["wood"]              = "Wood",
            ["stones"]            = "Stones",
            ["sulfur"]            = "Sulfur",
            ["gunpowder"]         = "Gunpowder",
            ["leather"]           = "Leather",
            ["fat.animal"]        = "Animal Fat",
            ["lowgradefuel"]      = "Low Grade Fuel",
            ["bone.fragments"]    = "Bone Fragments",
            ["charcoal"]          = "Charcoal",
            ["crude.oil"]         = "Crude Oil",
            ["riflebody"]         = "Rifle Body",
            ["semibody"]          = "Semi Body",
            ["smgbody"]           = "SMG Body",
            ["metalpipe"]         = "Metal Pipe",
            ["metalspring"]       = "Metal Spring",
            ["gears"]             = "Gears",
            ["metalblade"]        = "Metal Blade",
            ["roadsigns"]         = "Road Signs",
            ["sheetmetal"]        = "Sheet Metal",
            ["sewingkit"]         = "Sewing Kit",
            ["tarp"]              = "Tarp",
            ["propanetank"]       = "Propane Tank",
            ["cctv.camera"]       = "CCTV Camera",
            ["targeting.computer"]= "Targeting Computer",
            ["fuse"]              = "Fuse",
        };

        private void LogDiag(string message)
        {
            try
            {
                var mainWin = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                mainWin?.AppendLog(message);
                
                string logPath = @"C:\Users\Jawad\.gemini\antigravity-ide\brain\c4d06e13-9fd0-4c38-9e9e-769d13bce6c7\scratch\recycler-log.txt";
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private void OnIconsUpdated()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var item in _allRecyclerItems)
                {
                    if (item.Icon == null)
                    {
                        item.Icon = MainWindow.ResolveItemIcon(0, item.ShortName, 40);
                    }
                }
                foreach (var output in Outputs)
                {
                    if (output.Icon == null)
                    {
                        output.Icon = MainWindow.ResolveItemIcon(0, output.ShortName, 24);
                    }
                }
            }));
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterItems();
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterItems();
        }

        private void FilterItems()
        {
            if (_allRecyclerItems == null) return;

            string filterText       = SearchTextBox?.Text ?? "";
            string selectedCategory = CategoryComboBox?.SelectedItem as string ?? "All Categories";

            _filteredItems = _allRecyclerItems.Where(item =>
            {
                bool matchesSearch = string.IsNullOrEmpty(filterText) ||
                                     item.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                                     item.ShortName.Contains(filterText, StringComparison.OrdinalIgnoreCase);

                bool matchesCategory = selectedCategory == "All Categories" ||
                                       item.Data.category == selectedCategory;

                return matchesSearch && matchesCategory;
            }).ToList();

            LoadFirstPage();
        }

        /// <summary>Resets the visible list to the first <see cref="PageSize"/> items from the current filter.</summary>
        private void LoadFirstPage()
        {
            Items.Clear();
            _loadedCount = 0;
            LoadNextPage();

            // Scroll back to top when filter changes
            if (InputsScrollViewer != null)
                InputsScrollViewer.ScrollToTop();
        }

        /// <summary>Appends the next batch of filtered items to the visible list.</summary>
        private void LoadNextPage()
        {
            int take = Math.Min(PageSize, _filteredItems.Count - _loadedCount);
            if (take <= 0) return;

            for (int i = _loadedCount; i < _loadedCount + take; i++)
                Items.Add(_filteredItems[i]);

            _loadedCount += take;
        }

        private void InputsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Trigger load-more when within 150px of the bottom
            if (sender is ScrollViewer sv &&
                sv.ScrollableHeight > 0 &&
                sv.VerticalOffset >= sv.ScrollableHeight - 150)
            {
                LoadNextPage();
            }
        }

        private void LoadItems()
        {
            LogDiag("[RecyclerOverlay] Loading recycling calculator database...");

            string jsonContent = "";
            bool loaded = false;
            string sourcePath = "";

            var baseDir = AppContext.BaseDirectory;
            var currDir = Directory.GetCurrentDirectory();
            var entryAsm = System.Reflection.Assembly.GetEntryAssembly();
            var entryDir = entryAsm != null ? Path.GetDirectoryName(entryAsm.Location) : null;

            var filePaths = new[]
            {
                Path.Combine(baseDir, "recycler-items.json"),
                Path.Combine(currDir, "recycler-items.json"),
                entryDir is null ? null : Path.Combine(entryDir, "recycler-items.json"),
                Path.Combine(baseDir, "assets", "recycler-items.json"),
                Path.Combine(baseDir, "data",   "recycler-items.json"),
                Path.Combine(baseDir, "Assets", "Data", "recycler-items.json"),
            };

            foreach (var path in filePaths)
            {
                if (path != null)
                {
                    LogDiag($"[RecyclerOverlay] Checking file path: {path}");
                    if (File.Exists(path))
                    {
                        try
                        {
                            jsonContent = File.ReadAllText(path, System.Text.Encoding.UTF8);
                            loaded = true;
                            sourcePath = path;
                            LogDiag($"[RecyclerOverlay] Loaded database from disk: {path}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            LogDiag($"[RecyclerOverlay] Error reading file: {ex.Message}");
                        }
                    }
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
                    LogDiag($"[RecyclerOverlay] Checking Pack URI: {uri}");
                    try
                    {
                        var sri = Application.GetResourceStream(new Uri(uri));
                        if (sri?.Stream != null)
                        {
                            using var r = new StreamReader(sri.Stream);
                            jsonContent = r.ReadToEnd();
                            loaded = true;
                            sourcePath = uri;
                            LogDiag($"[RecyclerOverlay] Loaded database from resource: {uri}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Tolerate resource exceptions during check
                    }
                }
            }

            if (loaded && !string.IsNullOrEmpty(jsonContent))
            {
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var parsedItems = JsonSerializer.Deserialize<List<RecyclerItemData>>(jsonContent, options);
                    if (parsedItems != null)
                    {
                        LogDiag($"[RecyclerOverlay] Successfully deserialized {parsedItems.Count} total items.");
                        var list = new List<RecyclerItemViewModel>();
                        foreach (var item in parsedItems)
                        {
                            if (item.canBeRecycled)
                            {
                                 var vm = new RecyclerItemViewModel
                                 {
                                     Id = item.id,
                                     ShortName = item.shortName,
                                     DisplayName = !string.IsNullOrEmpty(item.displayName)
                                                   ? item.displayName
                                                   : MainWindow.ResolveItemName(0, item.shortName),
                                     StackSize = item.stackSize > 0 ? item.stackSize : 1,
                                     Data = item,
                                     Icon = MainWindow.ResolveItemIcon(0, item.shortName, 40)
                                 };
                                vm.QuantityChanged += (s, e) => CalculateYields();
                                list.Add(vm);
                            }
                        }

                        list = list.OrderBy(x => x.DisplayName).ToList();
                        LogDiag($"[RecyclerOverlay] Filtered to {list.Count} recyclable components.");

                        _allRecyclerItems = list;

                        // Dynamic Category Extraction
                        var categories = list.Select(x => x.Data.category)
                                             .Distinct()
                                             .Where(c => !string.IsNullOrEmpty(c))
                                             .OrderBy(c => c)
                                             .ToList();

                        CategoryComboBox.Items.Clear();
                        CategoryComboBox.Items.Add("All Categories");
                        foreach (var cat in categories)
                        {
                            CategoryComboBox.Items.Add(cat);
                        }
                        CategoryComboBox.SelectedIndex = 0; // Triggers FilterItems()
                    }
                    else
                    {
                        LogDiag("[RecyclerOverlay] Deserialized item list is null.");
                    }
                }
                catch (Exception ex)
                {
                    LogDiag($"[RecyclerOverlay] JSON Deserialization failed: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Failed to load recycler items: {ex.Message}");
                }
            }
            else
            {
                LogDiag("[RecyclerOverlay] Failed to load json content from any source.");
            }
        }

        private void CalculateYields()
        {
            // Accumulate all yields dynamically across every active item
            var wildYields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var safeYields = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in _allRecyclerItems)
            {
                if (item.Quantity <= 0) continue;

                if (item.Data?.recycleInfo == null) continue;

                foreach (var rec in item.Data.recycleInfo)
                {
                    var isWild = rec.recyclerId == "recycler-radtown";
                    var isSafe = rec.recyclerId == "recycler-safezone";
                    if (!isWild && !isSafe) continue;

                    void Accumulate(Dictionary<string, double> dict, string shortName, double amount)
                    {
                        if (!dict.ContainsKey(shortName)) dict[shortName] = 0.0;
                        dict[shortName] += amount;
                    }

                    if (rec.guaranteedOutput != null)
                    {
                        foreach (var outItem in rec.guaranteedOutput)
                        {
                            if (string.IsNullOrEmpty(outItem.itemId)) continue;
                            double val = item.Quantity * outItem.amount;
                            if (isWild) Accumulate(wildYields, outItem.itemId, val);
                            else        Accumulate(safeYields, outItem.itemId, val);
                        }
                    }

                    if (rec.percentageBasedOutput != null)
                    {
                        foreach (var outItem in rec.percentageBasedOutput)
                        {
                            if (string.IsNullOrEmpty(outItem.itemId)) continue;
                            // amount in JSON is already a 0-100 percentage
                            double val = item.Quantity * (outItem.amount / 100.0);
                            if (isWild) Accumulate(wildYields, outItem.itemId, val);
                            else        Accumulate(safeYields, outItem.itemId, val);
                        }
                    }
                }
            }

            // Rebuild the Outputs collection with every resource that has a non-zero yield
            var allShortNames = wildYields.Keys.Union(safeYields.Keys).Distinct().ToList();

            // Keep existing VMs where possible to avoid flicker; add/remove as needed
            var existingByShort = Outputs.ToDictionary(o => o.ShortName, StringComparer.OrdinalIgnoreCase);
            var toKeep = new HashSet<string>(allShortNames.Where(s => wildYields.GetValueOrDefault(s) > 0 || safeYields.GetValueOrDefault(s) > 0), StringComparer.OrdinalIgnoreCase);

            // Remove outputs no longer active
            foreach (var old in Outputs.Where(o => !toKeep.Contains(o.ShortName)).ToList())
                Outputs.Remove(old);

            // Update / add
            foreach (var sn in allShortNames.OrderBy(s => s))
            {
                double wild = wildYields.GetValueOrDefault(sn);
                double safe = safeYields.GetValueOrDefault(sn);
                if (wild <= 0 && safe <= 0) continue;

                if (existingByShort.TryGetValue(sn, out var vm))
                {
                    vm.WildAmount = wild;
                    vm.SafeAmount = safe;
                }
                else
                {
                    string display = _knownOutputNames.TryGetValue(sn, out var d) ? d
                                     : MainWindow.ResolveItemName(0, sn);
                    if (string.IsNullOrEmpty(display) || display == sn)
                        display = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                                    .ToTitleCase(sn.Replace(".", " ").Replace("_", " "));
                    var newVm = new RecyclerOutputViewModel
                    {
                        ShortName   = sn,
                        DisplayName = display,
                        Icon        = MainWindow.ResolveItemIcon(0, sn, 24),
                        WildAmount  = wild,
                        SafeAmount  = safe
                    };
                    Outputs.Add(newVm);
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, e);
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _allRecyclerItems)
            {
                item.Quantity = 0;
            }
        }

        private void BtnFillStacks_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items)
            {
                item.Quantity = item.StackSize > 0 ? item.StackSize : 10;
            }
        }

        private void ItemCard_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is RecyclerItemViewModel vm)
            {
                int delta = e.Delta > 0 ? 1 : -1;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) delta *= 10;
                vm.Quantity = Math.Max(0, vm.Quantity + delta);
                e.Handled = true;
            }
        }

        private void ItemCard_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is RecyclerItemViewModel vm)
            {
                if (e.OriginalSource is TextBox || e.OriginalSource is ScrollViewer || IsChildOfTextBox(e.OriginalSource as DependencyObject))
                    return;

                int amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                vm.Quantity += amount;
                e.Handled = true;
            }
        }

        private void ItemCard_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is RecyclerItemViewModel vm)
            {
                if (e.OriginalSource is TextBox || e.OriginalSource is ScrollViewer || IsChildOfTextBox(e.OriginalSource as DependencyObject))
                    return;

                int amount = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                vm.Quantity = Math.Max(0, vm.Quantity - amount);
                e.Handled = true;
            }
        }

        private bool IsChildOfTextBox(DependencyObject obj)
        {
            while (obj != null)
            {
                if (obj is TextBox) return true;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
        }

        private void QuantityTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void QuantityTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!int.TryParse(text, out _))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private void QuantityTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.SelectAll();
            }
        }

        private void QuantityTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (string.IsNullOrWhiteSpace(tb.Text) || !int.TryParse(tb.Text, out _))
                {
                    tb.Text = "0";
                }
            }
        }
    }

    public class RecyclerItemViewModel : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string ShortName { get; set; }
        public string DisplayName { get; set; }
        public int StackSize { get; set; }

        private ImageSource _icon;
        public ImageSource Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = Math.Max(0, value);
                    OnPropertyChanged(nameof(Quantity));
                    QuantityChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public RecyclerItemData Data { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler QuantityChanged;

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RecyclerOutputViewModel : INotifyPropertyChanged
    {
        private double _wildAmount;
        public double WildAmount
        {
            get => _wildAmount;
            set
            {
                _wildAmount = value;
                OnPropertyChanged(nameof(WildAmount));
                OnPropertyChanged(nameof(WildText));
                OnPropertyChanged(nameof(IsActive));
            }
        }

        private double _safeAmount;
        public double SafeAmount
        {
            get => _safeAmount;
            set
            {
                _safeAmount = value;
                OnPropertyChanged(nameof(SafeAmount));
                OnPropertyChanged(nameof(SafeText));
                OnPropertyChanged(nameof(IsActive));
            }
        }

        public string ShortName { get; set; }
        public string DisplayName { get; set; }

        private ImageSource _icon;
        public ImageSource Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        public bool IsActive => WildAmount > 0 || SafeAmount > 0;
        public string WildText => WildAmount > 0 ? WildAmount.ToString("0.#") : "0";
        public string SafeText => SafeAmount > 0 ? SafeAmount.ToString("0.#") : "0";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RecyclerItemData
    {
        public string id { get; set; }
        public string shortName { get; set; }
        public string category { get; set; }
        public string displayName { get; set; }
        public int stackSize { get; set; }
        public bool canBeRecycled { get; set; }
        public List<RecycleInfoData> recycleInfo { get; set; }
    }

    public class RecycleInfoData
    {
        public string recyclerId { get; set; }
        public string recyclerLink { get; set; }
        public List<RecycleOutputData> guaranteedOutput { get; set; }
        public List<RecycleOutputData> percentageBasedOutput { get; set; }
    }

    public class RecycleOutputData
    {
        public string itemId { get; set; }
        public string itemLink { get; set; }
        /// <summary>Guaranteed quantity (whole units) or percentage chance (0-100) for probabilistic items.</summary>
        public double amount { get; set; }
    }
}
