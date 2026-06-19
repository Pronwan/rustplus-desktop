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

        public RecyclerOverlay()
        {
            try
            {
                InitializeComponent();
                InputsControl.ItemsSource = Items;
                OutputsControl.ItemsSource = Outputs;

                InitializeOutputs();
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

        private void InitializeOutputs()
        {
            var outputsList = new List<RecyclerOutputViewModel>
            {
                new() { ShortName = "scrap", DisplayName = "Scrap", Icon = MainWindow.ResolveItemIcon(0, "scrap", 24) },
                new() { ShortName = "metal.refined", DisplayName = "High Quality Metal", Icon = MainWindow.ResolveItemIcon(0, "metal.refined", 24) },
                new() { ShortName = "metal.fragments", DisplayName = "Metal Fragments", Icon = MainWindow.ResolveItemIcon(0, "metal.fragments", 24) },
                new() { ShortName = "cloth", DisplayName = "Cloth", Icon = MainWindow.ResolveItemIcon(0, "cloth", 24) },
                new() { ShortName = "rope", DisplayName = "Rope", Icon = MainWindow.ResolveItemIcon(0, "rope", 24) },
                new() { ShortName = "techparts", DisplayName = "Tech Parts", Icon = MainWindow.ResolveItemIcon(0, "techparts", 24) }
            };

            Outputs.Clear();
            foreach (var outVm in outputsList)
            {
                Outputs.Add(outVm);
            }
        }

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

            string filterText = SearchTextBox?.Text ?? "";
            string selectedCategory = CategoryComboBox?.SelectedItem as string ?? "All Categories";

            Items.Clear();
            foreach (var item in _allRecyclerItems)
            {
                bool matchesSearch = string.IsNullOrEmpty(filterText) || 
                                     item.DisplayName.Contains(filterText, StringComparison.OrdinalIgnoreCase) || 
                                     item.ShortName.Contains(filterText, StringComparison.OrdinalIgnoreCase);

                bool matchesCategory = selectedCategory == "All Categories" || 
                                       item.Data.category == selectedCategory;

                if (matchesSearch && matchesCategory)
                {
                    Items.Add(item);
                }
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
                                    DisplayName = MainWindow.ResolveItemName(0, item.shortName),
                                    StackSize = item.stackSize,
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

        private string MapResourceShortName(string rawId)
        {
            if (rawId == "metal-refined") return "metal.refined";
            if (rawId == "metal-fragments") return "metal.fragments";
            return rawId;
        }

        private void CalculateYields()
        {
            var wildYields = new Dictionary<string, double>();
            var safeYields = new Dictionary<string, double>();

            foreach (var output in Outputs)
            {
                wildYields[output.ShortName] = 0.0;
                safeYields[output.ShortName] = 0.0;
            }

            foreach (var item in _allRecyclerItems)
            {
                if (item.Quantity <= 0) continue;

                if (item.Data?.recycleInfo != null)
                {
                    foreach (var rec in item.Data.recycleInfo)
                    {
                        var targetDict = rec.recyclerId == "recycler-radtown" ? wildYields : 
                                         rec.recyclerId == "recycler-safezone" ? safeYields : null;

                        if (targetDict == null) continue;

                        if (rec.guaranteedOutput != null)
                        {
                            foreach (var outItem in rec.guaranteedOutput)
                            {
                                string shortName = MapResourceShortName(outItem.itemId);
                                if (targetDict.ContainsKey(shortName))
                                {
                                    targetDict[shortName] += item.Quantity * outItem.amount;
                                }
                            }
                        }

                        if (rec.percentageBasedOutput != null)
                        {
                            foreach (var outItem in rec.percentageBasedOutput)
                            {
                                string shortName = MapResourceShortName(outItem.itemId);
                                if (targetDict.ContainsKey(shortName))
                                {
                                    targetDict[shortName] += item.Quantity * (outItem.amount * 0.01);
                                }
                            }
                        }
                    }
                }
            }

            foreach (var output in Outputs)
            {
                output.WildAmount = wildYields[output.ShortName];
                output.SafeAmount = safeYields[output.ShortName];
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
        public int stackSize { get; set; }
        public bool canBeRecycled { get; set; }
        public List<RecycleInfoData> recycleInfo { get; set; }
    }

    public class RecycleInfoData
    {
        public string recyclerId { get; set; }
        public List<RecycleOutputData> guaranteedOutput { get; set; }
        public List<RecycleOutputData> percentageBasedOutput { get; set; }
    }

    public class RecycleOutputData
    {
        public string itemId { get; set; }
        public int amount { get; set; }
    }
}
