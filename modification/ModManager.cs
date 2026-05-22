using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Views;

namespace RustPlusDesk.Modification
{
    public class ModManager
    {
        private readonly MainWindow _mainWindow;
        private readonly Action<string> _logAction;
        private readonly List<IMod> _mods = new();
        private readonly string _settingsPath;
        private bool _isUpdatingUI;

        public ModManager(MainWindow mainWindow, Action<string> logAction)
        {
            _mainWindow = mainWindow;
            _logAction = logAction;
            
            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");
            _settingsPath = Path.Combine(appDir, "modifications.json");
        }

        public void Initialize()
        {
            // Register mods here
            _mods.Add(new SmartSwitchesMod());
            _mods.Add(new GridCustomizationMod());

            // Load saved settings
            LoadSettings();

            // Initialize all mods
            foreach (var mod in _mods)
            {
                try
                {
                    mod.Initialize(_mainWindow);
                    _logAction($"Loaded mod: {mod.Name} (Enabled: {mod.IsEnabled})");
                }
                catch (Exception ex)
                {
                    _logAction($"Failed to initialize mod {mod.Name}: {ex.Message}");
                }
            }
        }

        public void OnChatReceived(string text, string author, ulong steamId)
        {
            foreach (var mod in _mods.Where(m => m.IsEnabled))
            {
                try
                {
                    mod.OnChatReceived(text, author, steamId);
                }
                catch (Exception ex)
                {
                    _logAction($"Error in mod {mod.Name} OnChatReceived: {ex.Message}");
                }
            }
        }

        public void OnDeviceStateChanged(uint entityId, bool isOn, string kind)
        {
            foreach (var mod in _mods.Where(m => m.IsEnabled))
            {
                try
                {
                    mod.OnDeviceStateChanged(entityId, isOn, kind);
                }
                catch (Exception ex)
                {
                    _logAction($"Error in mod {mod.Name} OnDeviceStateChanged: {ex.Message}");
                }
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                if (settings == null) return;

                foreach (var mod in _mods)
                {
                    if (settings.TryGetValue(mod.Id, out bool isEnabled))
                    {
                        mod.IsEnabled = isEnabled;
                    }
                }
            }
            catch (Exception ex)
            {
                _logAction($"Failed to load mod settings: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                var settings = _mods.ToDictionary(m => m.Id, m => m.IsEnabled);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                _logAction($"Failed to save mod settings: {ex.Message}");
            }
        }

        public Grid CreateModsTabUI()
        {
            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left side list box
            var listBox = new ListBox
            {
                Margin = new Thickness(10),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = (Brush)Application.Current.FindResource("CardStrokeColorDefaultBrush")
            };

            // Right side detail panel
            var detailPanel = new StackPanel
            {
                Margin = new Thickness(20),
                Visibility = Visibility.Collapsed
            };

            var modTitle = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var modDesc = new TextBlock
            {
                FontSize = 14,
                Foreground = (Brush)Application.Current.FindResource("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };

            var toggleSwitch = new Wpf.Ui.Controls.ToggleSwitch
            {
                Content = "Enable Modification",
                Margin = new Thickness(0, 0, 0, 20)
            };

            var customUiContainer = new ContentControl
            {
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };

            detailPanel.Children.Add(modTitle);
            detailPanel.Children.Add(modDesc);
            detailPanel.Children.Add(toggleSwitch);
            detailPanel.Children.Add(customUiContainer);

            Grid.SetColumn(listBox, 0);
            Grid.SetColumn(detailPanel, 1);

            mainGrid.Children.Add(listBox);
            mainGrid.Children.Add(detailPanel);

            // Populate mods
            foreach (var mod in _mods)
            {
                var itemGrid = new Grid { Margin = new Thickness(5) };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameText = new TextBlock
                {
                    Text = mod.Name,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var statusText = new TextBlock
                {
                    Text = mod.IsEnabled ? "Enabled" : "Disabled",
                    FontSize = 11,
                    Foreground = mod.IsEnabled ? Brushes.LimeGreen : Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };

                Grid.SetColumn(nameText, 0);
                Grid.SetColumn(statusText, 1);
                itemGrid.Children.Add(nameText);
                itemGrid.Children.Add(statusText);

                var listBoxItem = new ListBoxItem
                {
                    Content = itemGrid,
                    Tag = mod,
                    Padding = new Thickness(10)
                };

                listBox.Items.Add(listBoxItem);
            }

            // Selection change logic
            listBox.SelectionChanged += (s, e) =>
            {
                if (listBox.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is IMod selectedMod)
                {
                    detailPanel.Visibility = Visibility.Visible;
                    modTitle.Text = selectedMod.Name;
                    modDesc.Text = selectedMod.Description;

                    _isUpdatingUI = true;
                    toggleSwitch.IsChecked = selectedMod.IsEnabled;
                    _isUpdatingUI = false;

                    customUiContainer.Content = selectedMod.GetConfigUI();
                }
                else
                {
                    detailPanel.Visibility = Visibility.Collapsed;
                    customUiContainer.Content = null;
                }
            };

            // Define toggle event handlers once
            toggleSwitch.Checked += (ts, ev) =>
            {
                if (_isUpdatingUI) return;
                HandleToggleChange(listBox, toggleSwitch, modTitle);
            };
            toggleSwitch.Unchecked += (ts, ev) =>
            {
                if (_isUpdatingUI) return;
                HandleToggleChange(listBox, toggleSwitch, modTitle);
            };

            return mainGrid;
        }

        private void HandleToggleChange(ListBox listBox, Wpf.Ui.Controls.ToggleSwitch toggleSwitch, TextBlock modTitle)
        {
            if (listBox.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is IMod selectedMod)
            {
                selectedMod.IsEnabled = toggleSwitch.IsChecked == true;
                SaveSettings();

                // Update status text in list item
                if (selectedItem.Content is Grid grid && grid.Children.OfType<TextBlock>().LastOrDefault() is TextBlock statusTxt)
                {
                    statusTxt.Text = selectedMod.IsEnabled ? "Enabled" : "Disabled";
                    statusTxt.Foreground = selectedMod.IsEnabled ? Brushes.LimeGreen : Brushes.Gray;
                }

                _logAction($"Mod {selectedMod.Name} toggled: {(selectedMod.IsEnabled ? "Enabled" : "Disabled")}");
            }
        }
    }
}
