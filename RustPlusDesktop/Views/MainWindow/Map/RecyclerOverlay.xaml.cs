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

        // Infinite scroll state
        private List<RecyclerItemViewModel> _filteredItems = new();
        private int  _loadedCount  = 0;
        private bool _isAppending  = false;   // prevents re-entrant appends
        private const int PageSize = 30;

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

            // Reset to first page
            Items.Clear();
            _loadedCount = 0;
            AppendNextPage();               // show first 30 immediately
            InputsScrollViewer?.ScrollToTop();
        }

        /// <summary>
        /// Appends the next <see cref="PageSize"/> items from the filtered list.
        /// Safe to call from any scroll event — guarded against re-entry.
        /// </summary>
        private void AppendNextPage()
        {
            if (_isAppending) return;
            if (_loadedCount >= _filteredItems.Count) return;

            _isAppending = true;
            try
            {
                int end = Math.Min(_loadedCount + PageSize, _filteredItems.Count);
                for (int i = _loadedCount; i < end; i++)
                    Items.Add(_filteredItems[i]);
                _loadedCount = end;
            }
            finally
            {
                _isAppending = false;
            }
        }

        private void InputsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // e.VerticalChange > 0  =>  user scrolled DOWN  (not a content-resize event)
            // Ignore upward scroll, filter resets, and content additions
            if (e.VerticalChange <= 0) return;

            if (sender is ScrollViewer sv &&
                sv.ScrollableHeight > 0 &&
                sv.VerticalOffset >= sv.ScrollableHeight - 200)
            {
                AppendNextPage();
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

            if (!loaded)
            {
                string entryName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "RustPlusDesk";
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var resName = $"{entryName}.Assets.Data.recycler-items.json";
                LogDiag($"[RecyclerOverlay] Checking embedded resource: {resName}");
                try
                {
                    using var stream = asm.GetManifestResourceStream(resName);
                    if (stream != null)
                    {
                        using var r = new StreamReader(stream);
                        jsonContent = r.ReadToEnd();
                        loaded = true;
                        sourcePath = resName;
                        LogDiag($"[RecyclerOverlay] Loaded database from embedded resource: {resName}");
                    }
                }
                catch (Exception ex)
                {
                    LogDiag($"[RecyclerOverlay] Embedded resource failed: {ex.Message}");
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
                        LoadStackSizes(list);

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

        private void LoadStackSizes(List<RecyclerItemViewModel> list)
        {
            try
            {
                string jsonContent = "";
                string asmName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "RustPlusDesk";
                var resName = $"{asmName}.Assets.Data.Recycling-Data.json";
                using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var r = new StreamReader(stream);
                    jsonContent = r.ReadToEnd();
                }
                else
                {
                    var filePaths = new[] {
                        Path.Combine(AppContext.BaseDirectory, "Assets", "Data", "Recycling-Data.json"),
                        Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Data", "Recycling-Data.json")
                    };
                    foreach (var p in filePaths)
                    {
                        if (File.Exists(p))
                        {
                            jsonContent = File.ReadAllText(p);
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(jsonContent))
                {
                    using var doc = JsonDocument.Parse(jsonContent);
                    var root = doc.RootElement;
                    foreach (var item in list)
                    {
                        if (root.TryGetProperty(item.ShortName, out var elem))
                        {
                            if (elem.TryGetProperty("stackSize", out var st))
                            {
                                item.StackSize = st.GetInt32();
                            }
                            if (elem.TryGetProperty("recycler", out var rec))
                            {
                                item.WildRecyclerNode = rec.Clone();
                            }
                            if (elem.TryGetProperty("safe-zone-recycler", out var safe))
                            {
                                item.SafeRecyclerNode = safe.Clone();
                            }
                        }
                    }
                    LogDiag($"[RecyclerOverlay] Successfully loaded stack sizes from Recycling-Data.json.");
                }
            }
            catch (Exception ex)
            {
                LogDiag($"[RecyclerOverlay] Failed to load stack sizes: {ex.Message}");
            }
        }

        private void CalculateYields()
        {
            var wildYields = new Dictionary<string, (double Expected, double Min, double Max)>(StringComparer.OrdinalIgnoreCase);
            var safeYields = new Dictionary<string, (double Expected, double Min, double Max)>(StringComparer.OrdinalIgnoreCase);
            double totalWildTime = 0;
            double totalSafeTime = 0;

            void ProcessDetailedNode(JsonElement? node, Dictionary<string, (double Expected, double Min, double Max)> dict, int qty)
            {
                if (node == null) return;
                if (node.Value.TryGetProperty("yield", out var yieldArr))
                {
                    foreach (var y in yieldArr.EnumerateArray())
                    {
                        if (y.TryGetProperty("shortname", out var snProp) && y.TryGetProperty("quantity", out var qProp) && y.TryGetProperty("probability", out var pProp))
                        {
                            string shortName = snProp.GetString() ?? "";
                            double quantity = qProp.GetDouble();
                            double probability = pProp.GetDouble();
                            
                            double expected = qty * quantity * probability;
                            double min = probability >= 1.0 ? qty * quantity : 0;
                            double max = qty * quantity;

                            if (!dict.TryGetValue(shortName, out var current))
                                current = (0, 0, 0);

                            dict[shortName] = (current.Expected + expected, current.Min + min, current.Max + max);
                        }
                    }
                }
            }

            foreach (var item in _allRecyclerItems)
            {
                if (item.Quantity <= 0) continue;

                int unitsPerTick = (int)Math.Ceiling(item.StackSize * 0.10);
                if (unitsPerTick < 1) unitsPerTick = 1;
                int ticks = (int)Math.Ceiling((double)item.Quantity / unitsPerTick);
                totalWildTime += ticks * 5;
                totalSafeTime += ticks * 8;

                if (item.WildRecyclerNode != null || item.SafeRecyclerNode != null)
                {
                    ProcessDetailedNode(item.WildRecyclerNode, wildYields, item.Quantity);
                    ProcessDetailedNode(item.SafeRecyclerNode, safeYields, item.Quantity);
                }
                else if (item.Data?.recycleInfo != null)
                {
                    foreach (var rec in item.Data.recycleInfo)
                    {
                        var isWild = rec.recyclerId == "recycler-radtown";
                        var isSafe = rec.recyclerId == "recycler-safezone";
                        if (!isWild && !isSafe) continue;
                        
                        var dict = isWild ? wildYields : safeYields;

                        if (rec.guaranteedOutput != null)
                        {
                            foreach (var outItem in rec.guaranteedOutput)
                            {
                                if (string.IsNullOrEmpty(outItem.itemId)) continue;
                                double expected = item.Quantity * outItem.amount;
                                if (!dict.TryGetValue(outItem.itemId, out var current)) current = (0,0,0);
                                dict[outItem.itemId] = (current.Expected + expected, current.Min + expected, current.Max + expected);
                            }
                        }
                        if (rec.percentageBasedOutput != null)
                        {
                            foreach (var outItem in rec.percentageBasedOutput)
                            {
                                if (string.IsNullOrEmpty(outItem.itemId)) continue;
                                double expected = item.Quantity * (outItem.amount / 100.0);
                                double max = item.Quantity;
                                if (!dict.TryGetValue(outItem.itemId, out var current)) current = (0,0,0);
                                dict[outItem.itemId] = (current.Expected + expected, current.Min, current.Max + max);
                            }
                        }
                    }
                }
            }

            var allShortNames = wildYields.Keys.Union(safeYields.Keys).Distinct().ToList();
            var existingByShort = Outputs.ToDictionary(o => o.ShortName, StringComparer.OrdinalIgnoreCase);
            var toKeep = new HashSet<string>(allShortNames.Where(s => wildYields.GetValueOrDefault(s).Expected > 0 || safeYields.GetValueOrDefault(s).Expected > 0), StringComparer.OrdinalIgnoreCase);

            foreach (var old in Outputs.Where(o => !toKeep.Contains(o.ShortName)).ToList())
                Outputs.Remove(old);

            foreach (var sn in allShortNames.OrderBy(s => s))
            {
                var wild = wildYields.GetValueOrDefault(sn);
                var safe = safeYields.GetValueOrDefault(sn);
                if (wild.Expected <= 0 && safe.Expected <= 0) continue;

                string BuildTooltip((double Expected, double Min, double Max) val)
                {
                    if (val.Min < val.Max) return $"Range: {val.Min:0.#} - {val.Max:0.#}";
                    return null;
                }

                if (existingByShort.TryGetValue(sn, out var vm))
                {
                    vm.WildAmount = wild.Expected;
                    vm.SafeAmount = safe.Expected;
                    vm.WildToolTip = BuildTooltip(wild);
                    vm.SafeToolTip = BuildTooltip(safe);
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
                        WildAmount  = wild.Expected,
                        SafeAmount  = safe.Expected,
                        WildToolTip = BuildTooltip(wild),
                        SafeToolTip = BuildTooltip(safe)
                    };
                    Outputs.Add(newVm);
                }
            }

            if (TxtWildTime != null)
                TxtWildTime.Text = TimeSpan.FromSeconds(totalWildTime).ToString(@"hh\:mm\:ss");
            if (TxtSafeTime != null)
                TxtSafeTime.Text = TimeSpan.FromSeconds(totalSafeTime).ToString(@"hh\:mm\:ss");
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
        public JsonElement? WildRecyclerNode { get; set; }
        public JsonElement? SafeRecyclerNode { get; set; }

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

        private string _wildToolTip;
        public string WildToolTip
        {
            get => _wildToolTip;
            set
            {
                _wildToolTip = value;
                OnPropertyChanged(nameof(WildToolTip));
            }
        }

        private string _safeToolTip;
        public string SafeToolTip
        {
            get => _safeToolTip;
            set
            {
                _safeToolTip = value;
                OnPropertyChanged(nameof(SafeToolTip));
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
        public string WildText => WildAmount > 0 ? Math.Round(WildAmount).ToString("0") : "0";
        public string SafeText => SafeAmount > 0 ? Math.Round(SafeAmount).ToString("0") : "0";

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
