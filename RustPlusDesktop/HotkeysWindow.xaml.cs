using RustPlusDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using static RustPlusDesk.Views.MainWindow;

namespace RustPlusDesk.Views
{


    public partial class HotkeysWindow : Window
    {
        public sealed class RowVM
        {
            public long EntityId { get; init; }
            public string Display { get; init; } = "";
            public string? Hotkey { get; set; }
        }

        

        private readonly List<RowVM> _rows;
        private readonly Dictionary<string, List<long>> _map;

        public HotkeysWindow(IEnumerable<SmartDevice> devices,
                             Dictionary<string, List<long>> hotkeyMap)
        {
            InitializeComponent();
            _map = hotkeyMap;

            _rows = devices
                .Where(d => string.Equals(d.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase))
                .Select(d => new RowVM
                {
                    EntityId = d.EntityId,
                    Display = d.Display,
                    Hotkey = FindGestureFor(d.EntityId)
                })
                .OrderBy(r => r.Display)
                .ToList();

            DataContext = _rows;
        }

        public bool ActivateOnClose { get; private set; }

        private void BtnCloseActivate_Click(object sender, RoutedEventArgs e)
        {
            ActivateOnClose = true;
            DialogResult = true;   // MainWindow interpretiert true = aktivieren
            Close();
        }

        private void BtnCloseDeactivate_Click(object sender, RoutedEventArgs e)
        {
            ActivateOnClose = false;
            DialogResult = false;  // MainWindow interpretiert false = deaktivieren
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

            // kleines Capture-Dialogfenster inline:
            var cap = new HotkeyCaptureDialog();
            if (cap.ShowDialog() == true && !string.IsNullOrWhiteSpace(cap.Gesture))
            {
                // Entity aus allen Gestures entfernen
                foreach (var list in _map.Values) list.Remove(row.EntityId);

                // Entity zu neuer Gesture hinzufügen
                if (!_map.TryGetValue(cap.Gesture!, out var l)) _map[cap.Gesture!] = l = new List<long>();
                if (!l.Contains(row.EntityId)) l.Add(row.EntityId);

                row.Hotkey = cap.Gesture!;
                GridDevices.Items.Refresh();
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not RowVM row) return;
            foreach (var list in _map.Values) list.Remove(row.EntityId);
            row.Hotkey = null;
            GridDevices.Items.Refresh();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }

    // sehr einfacher Dialog zum Einfangen eines KeyGestures
    public sealed class HotkeyCaptureDialog : Window
    {
        public string? Gesture { get; private set; }
        public HotkeyCaptureDialog()
        {
            Title = "Press hotkey…";
            Width = 320; Height = 120;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "Press the desired key combination",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.White
            };
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 34, 34));
            PreviewKeyDown += OnPreviewKeyDown;
        }
        private void OnPreviewKeyDown(object? s, KeyEventArgs e)
        {
            if (e.Key == Key.System) return;
            var key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin) return;

            bool ctrl = (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl));
            bool alt = (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt));
            bool shift = (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
            bool win = (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin));

            Gesture = GlobalHotkeyManager.Format(key, ctrl, alt, shift, win);
            DialogResult = true;
            e.Handled = true;
        }
    }
}
