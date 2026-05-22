using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Views;

namespace RustPlusDesk.Modification
{
    public class GridCustomizationMod : IMod
    {
        public string Id => "GridCustomization";
        public string Name => "Grid Customization";
        public string Description => "Allows manual shifting of the coordinate grid horizontally (Left/Right) and vertically (Down/Up). " +
                                     "The shift instantly affects the visual map grid lines and coordinate log/chat calculations.";

        private bool _isEnabled = false;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                ApplyOrResetGridShift();
            }
        }

        private MainWindow? _mainWindow;
        private double _shiftX = 0.0;
        private double _shiftY = 0.0;

        private Slider? _sliderX;
        private Slider? _sliderY;
        private TextBox? _txtXValue;
        private TextBox? _txtYValue;
        private bool _isUpdatingUIFromDrag = false;

        private readonly string _settingsPath;

        public GridCustomizationMod()
        {
            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");
            _settingsPath = Path.Combine(appDir, "grid_settings.json");
            LoadSettings();
        }

        public void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            if (_isEnabled)
            {
                ApplyGridShift();
            }
        }

        public void OnChatReceived(string text, string author, ulong steamId) { }
        public void OnDeviceStateChanged(uint entityId, bool isOn, string kind) { }

        private class GridSettings
        {
            public double ShiftX { get; set; }
            public double ShiftY { get; set; }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<GridSettings>(json);
                    if (settings != null)
                    {
                        _shiftX = settings.ShiftX;
                        _shiftY = settings.ShiftY;
                    }
                }
            }
            catch
            {
                // Ignore load failures
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new GridSettings { ShiftX = _shiftX, ShiftY = _shiftY };
                var json = JsonSerializer.Serialize(settings);
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Ignore save failures
            }
        }

        private void ApplyGridShift()
        {
            if (_mainWindow != null)
            {
                _mainWindow.GridShiftX = _shiftX;
                _mainWindow.GridShiftY = _shiftY;
                _mainWindow.ModRedrawGrid();
            }
        }

        private void ApplyOrResetGridShift()
        {
            if (_mainWindow == null) return;

            if (_isEnabled)
            {
                _mainWindow.GridShiftX = _shiftX;
                _mainWindow.GridShiftY = _shiftY;
            }
            else
            {
                _mainWindow.GridShiftX = 0.0;
                _mainWindow.GridShiftY = 0.0;
                _mainWindow.IsDragGridMode = false;
            }
            _mainWindow.ModRedrawGrid();
        }

        private void HandleGridOffsetsDragged(double newX, double newY)
        {
            if (_isUpdatingUIFromDrag) return;
            _isUpdatingUIFromDrag = true;
            try
            {
                _shiftX = newX;
                _shiftY = newY;
                if (_sliderX != null) _sliderX.Value = newX;
                if (_sliderY != null) _sliderY.Value = newY;
                if (_txtXValue != null) _txtXValue.Text = newX.ToString("F0");
                if (_txtYValue != null) _txtYValue.Text = newY.ToString("F0");
                SaveSettings();
            }
            finally
            {
                _isUpdatingUIFromDrag = false;
            }
        }

        public FrameworkElement? GetConfigUI()
        {
            var root = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

            // Direct Map Calibration (Drag Grid) Toggle
            var toggleDragGrid = new Wpf.Ui.Controls.ToggleSwitch
            {
                Content = "Drag Grid on Map (Left Click & Drag)",
                IsChecked = _mainWindow?.IsDragGridMode == true,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = Brushes.White
            };
            toggleDragGrid.Checked += (s, e) =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.IsDragGridMode = true;
                    _mainWindow.ModLog("Direct Map Calibration enabled. Hold left mouse button on the map and drag the grid lines.");
                }
            };
            toggleDragGrid.Unchecked += (s, e) =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.IsDragGridMode = false;
                    _mainWindow.ModLog("Direct Map Calibration disabled.");
                }
            };
            root.Children.Add(toggleDragGrid);

            // Horizontal Shift Slider (Left/Right)
            var lblX = new TextBlock
            {
                Text = "Horizontal Shift (meters)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            root.Children.Add(lblX);

            var gridX = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            gridX.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridX.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); // space
            gridX.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // TextBox

            _sliderX = new Slider
            {
                Minimum = -300,
                Maximum = 300,
                Value = _shiftX,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            _sliderX.ValueChanged += (s, e) =>
            {
                if (_isUpdatingUIFromDrag) return;
                _shiftX = Math.Round(e.NewValue);
                if (_txtXValue != null)
                {
                    _txtXValue.Text = _shiftX.ToString("F0");
                }
                if (_isEnabled)
                {
                    SaveSettings();
                    ApplyGridShift();
                }
            };
            Grid.SetColumn(_sliderX, 0);
            gridX.Children.Add(_sliderX);

            _txtXValue = new TextBox
            {
                Text = _shiftX.ToString("F0"),
                Width = 70,
                Height = 26,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                ToolTip = "Enter offset in meters manually. Use Up/Down arrows to step by 1.",
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Gray,
                Padding = new Thickness(2)
            };
            _txtXValue.TextChanged += (s, e) =>
            {
                if (_isUpdatingUIFromDrag) return;
                if (double.TryParse(_txtXValue.Text, out double val))
                {
                    double clamped = Math.Clamp(val, -300.0, 300.0);
                    if (Math.Abs(_shiftX - clamped) > 0.001)
                    {
                        _shiftX = clamped;
                        if (_sliderX != null && Math.Abs(_sliderX.Value - clamped) > 0.001)
                        {
                            _sliderX.Value = clamped;
                        }
                        if (_isEnabled)
                        {
                            SaveSettings();
                            ApplyGridShift();
                        }
                    }
                }
            };
            _txtXValue.LostFocus += (s, e) =>
            {
                _txtXValue.Text = _shiftX.ToString("F0");
            };
            _txtXValue.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    UIElement element = (UIElement)s;
                    element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            };
            _txtXValue.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Up)
                {
                    double currentVal = _shiftX;
                    double newVal = Math.Clamp(currentVal + 1, -300.0, 300.0);
                    _txtXValue.Text = newVal.ToString("F0");
                    _txtXValue.CaretIndex = _txtXValue.Text.Length;
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    double currentVal = _shiftX;
                    double newVal = Math.Clamp(currentVal - 1, -300.0, 300.0);
                    _txtXValue.Text = newVal.ToString("F0");
                    _txtXValue.CaretIndex = _txtXValue.Text.Length;
                    e.Handled = true;
                }
            };
            Grid.SetColumn(_txtXValue, 2);
            gridX.Children.Add(_txtXValue);

            root.Children.Add(gridX);

            // Vertical Shift Slider (Down/Up)
            var lblY = new TextBlock
            {
                Text = "Vertical Shift (meters)",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            root.Children.Add(lblY);

            var gridY = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            gridY.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridY.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); // space
            gridY.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // TextBox

            _sliderY = new Slider
            {
                Minimum = -300,
                Maximum = 300,
                Value = _shiftY,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            _sliderY.ValueChanged += (s, e) =>
            {
                if (_isUpdatingUIFromDrag) return;
                _shiftY = Math.Round(e.NewValue);
                if (_txtYValue != null)
                {
                    _txtYValue.Text = _shiftY.ToString("F0");
                }
                if (_isEnabled)
                {
                    SaveSettings();
                    ApplyGridShift();
                }
            };
            Grid.SetColumn(_sliderY, 0);
            gridY.Children.Add(_sliderY);

            _txtYValue = new TextBox
            {
                Text = _shiftY.ToString("F0"),
                Width = 70,
                Height = 26,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                ToolTip = "Enter offset in meters manually. Use Up/Down arrows to step by 1.",
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Gray,
                Padding = new Thickness(2)
            };
            _txtYValue.TextChanged += (s, e) =>
            {
                if (_isUpdatingUIFromDrag) return;
                if (double.TryParse(_txtYValue.Text, out double val))
                {
                    double clamped = Math.Clamp(val, -300.0, 300.0);
                    if (Math.Abs(_shiftY - clamped) > 0.001)
                    {
                        _shiftY = clamped;
                        if (_sliderY != null && Math.Abs(_sliderY.Value - clamped) > 0.001)
                        {
                            _sliderY.Value = clamped;
                        }
                        if (_isEnabled)
                        {
                            SaveSettings();
                            ApplyGridShift();
                        }
                    }
                }
            };
            _txtYValue.LostFocus += (s, e) =>
            {
                _txtYValue.Text = _shiftY.ToString("F0");
            };
            _txtYValue.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    UIElement element = (UIElement)s;
                    element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            };
            _txtYValue.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Up)
                {
                    double currentVal = _shiftY;
                    double newVal = Math.Clamp(currentVal + 1, -300.0, 300.0);
                    _txtYValue.Text = newVal.ToString("F0");
                    _txtYValue.CaretIndex = _txtYValue.Text.Length;
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    double currentVal = _shiftY;
                    double newVal = Math.Clamp(currentVal - 1, -300.0, 300.0);
                    _txtYValue.Text = newVal.ToString("F0");
                    _txtYValue.CaretIndex = _txtYValue.Text.Length;
                    e.Handled = true;
                }
            };
            Grid.SetColumn(_txtYValue, 2);
            gridY.Children.Add(_txtYValue);

            root.Children.Add(gridY);

            // Reset Button
            var btnReset = new Button
            {
                Content = "Reset Offsets",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(15, 8, 15, 8)
            };
            btnReset.Click += (s, e) =>
            {
                _isUpdatingUIFromDrag = true;
                try
                {
                    _shiftX = 0.0;
                    _shiftY = 0.0;
                    if (_sliderX != null) _sliderX.Value = 0.0;
                    if (_sliderY != null) _sliderY.Value = 0.0;
                    if (_txtXValue != null) _txtXValue.Text = "0";
                    if (_txtYValue != null) _txtYValue.Text = "0";
                    if (_isEnabled)
                    {
                        SaveSettings();
                        ApplyGridShift();
                    }
                }
                finally
                {
                    _isUpdatingUIFromDrag = false;
                }
            };
            root.Children.Add(btnReset);

            // Loaded & Unloaded Lifecycle Subscriptions
            root.Loaded += (s, e) =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.OnGridOffsetsDragged -= HandleGridOffsetsDragged;
                    _mainWindow.OnGridOffsetsDragged += HandleGridOffsetsDragged;
                }
            };
            root.Unloaded += (s, e) =>
            {
                if (_mainWindow != null)
                {
                    _mainWindow.IsDragGridMode = false;
                    _mainWindow.OnGridOffsetsDragged -= HandleGridOffsetsDragged;
                }
            };

            return root;
        }
    }
}
