using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Views.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RustPlusDesk.Views
{
    public partial class HotkeysWindow : Window
    {
        public sealed class RowVM
        {
            public long EntityId { get; init; }
            public string DisplayName { get; init; } = "";
            public string Status { get; init; } = "";
            public bool IsDeleted { get; init; }
            public string? Hotkey { get; set; }
            public bool IsRegisteredWarningVisible { get; set; }

            public bool HasHotkey => !string.IsNullOrEmpty(Hotkey);
        }

        private readonly List<RowVM> _rows;
        private readonly Dictionary<string, List<long>> _map;
        private readonly HotkeyOptions _options;
        private readonly IReadOnlyDictionary<string, bool>? _regStatus;

        public HotkeysWindow(IEnumerable<SmartDevice> devices,
                             Dictionary<string, List<long>> hotkeyMap,
                             HotkeyOptions options,
                             IReadOnlyDictionary<string, bool>? regStatus = null)
        {
            InitializeComponent();
            _map = hotkeyMap;
            _options = options;
            _regStatus = regStatus;

            var existingIds = devices.Select(d => d.EntityId).ToHashSet();
            var tempList = devices
                .Where(d => string.Equals(d.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase))
                .Select(d => new RowVM
                {
                    EntityId = d.EntityId,
                    DisplayName = d.DisplayName,
                    Status = d.IsOn is bool b ? (b ? "ON" : "OFF") : "–",
                    IsDeleted = false,
                    Hotkey = FindGestureFor(d.EntityId),
                    IsRegisteredWarningVisible = false
                })
                .ToList();

            foreach (var kv in _map)
            {
                foreach (var id in kv.Value)
                {
                    if (!existingIds.Contains((uint)id) && !tempList.Any(r => r.EntityId == id))
                    {
                        tempList.Add(new RowVM
                        {
                            EntityId = id,
                            DisplayName = "Unknown Device",
                            Status = "DELETED",
                            IsDeleted = true,
                            Hotkey = kv.Key,
                            IsRegisteredWarningVisible = false
                        });
                    }
                }
            }

            _rows = tempList.OrderBy(r => r.DisplayName).ToList();
            
            // Set warning visibility flag
            foreach (var row in _rows)
            {
                row.IsRegisteredWarningVisible = IsHotkeyWarningVisible(row.Hotkey);
            }

            DataContext = _rows;

            // Load options
            CbMode.SelectedIndex = _options.ParallelMode ? 1 : 0;
            SldDelay.Value = _options.ToggleDelayMs;
            PanelDelay.Visibility = _options.ParallelMode ? Visibility.Collapsed : Visibility.Visible;
            TxtDelayLabel.Text = $"Toggle Delay: {_options.ToggleDelayMs}ms";
        }

        private bool IsHotkeyWarningVisible(string? hotkey)
        {
            if (string.IsNullOrEmpty(hotkey)) return false;
            if (_regStatus != null && _regStatus.TryGetValue(hotkey, out var success))
            {
                return !success;
            }
            return false;
        }

        public bool ActivateOnClose { get; private set; }

        private void BtnCloseActivate_Click(object sender, RoutedEventArgs e)
        {
            ActivateOnClose = true;
            DialogResult = true;
            Close();
        }

        private void BtnCloseDeactivate_Click(object sender, RoutedEventArgs e)
        {
            ActivateOnClose = false;
            DialogResult = false;
            Close();
        }

        private string? FindGestureFor(long entityId)
        {
            foreach (var kv in _map)
                if (kv.Value.Contains(entityId))
                    return kv.Key;
            return null;
        }

        private void BtnSet_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not RowVM row) return;

            var cap = new HotkeyCaptureWindow { Owner = this };
            if (cap.ShowDialog() == true && !string.IsNullOrWhiteSpace(cap.Gesture))
            {
                // Entity aus allen Gestures entfernen
                foreach (var list in _map.Values) list.Remove(row.EntityId);

                // Entity zu neuer Gesture hinzufügen
                if (!_map.TryGetValue(cap.Gesture!, out var l)) _map[cap.Gesture!] = l = new List<long>();
                if (!l.Contains(row.EntityId)) l.Add(row.EntityId);

                row.Hotkey = cap.Gesture!;
                row.IsRegisteredWarningVisible = IsHotkeyWarningVisible(row.Hotkey);
                GridDevices.Items.Refresh();
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not RowVM row) return;
            foreach (var list in _map.Values) list.Remove(row.EntityId);
            row.Hotkey = null;
            row.IsRegisteredWarningVisible = false;
            GridDevices.Items.Refresh();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void SldDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDelayLabel == null || _options == null) return;
            int val = (int)e.NewValue;
            TxtDelayLabel.Text = $"Toggle Delay: {val}ms";
            _options.ToggleDelayMs = val;
        }

        private void CbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbMode == null || PanelDelay == null || _options == null) return;
            bool parallel = CbMode.SelectedIndex == 1;
            _options.ParallelMode = parallel;
            PanelDelay.Visibility = parallel ? Visibility.Collapsed : Visibility.Visible;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
