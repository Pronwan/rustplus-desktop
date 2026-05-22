using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using RustPlusDesk.Modification;

namespace RustPlusDesk.Views
{
    public partial class MainWindow
    {
        private ModManager? _modManager;

        public void InitializeModifications()
        {
            _modManager = new ModManager(this, AppendLog);
            _modManager.Initialize();

            // Add the Mods tab to the main TabControl programmatically
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var tabItem = new TabItem
                    {
                        Header = "🛠 Mods",
                        Style = (Style)FindResource("PrettyTabItem")
                    };

                    // Create the UI content for the modifications tab
                    var grid = _modManager.CreateModsTabUI();
                    tabItem.Content = grid;

                    MainTabs.Items.Add(tabItem);
                    AppendLog("[ModSystem] Added 'Mods' tab to MainTabs.");
                }
                catch (Exception ex)
                {
                    AppendLog($"[ModSystem] Error creating Mods UI tab: {ex.Message}");
                }
            });

            // Register event handlers on the WebSocket client
            if (_rust is RustPlusClientReal real)
            {
                real.DeviceStateEvent += async (id, isOn, kind) =>
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _modManager.OnDeviceStateChanged(id, isOn, kind ?? "");
                    });
                };

                real.TeamChatReceived += async (sender, m) =>
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _modManager.OnChatReceived(m.Text, m.Author, m.SteamId);
                    });
                };
            }
        }

        // Helper methods for modifications

        public void ModLog(string message)
        {
            AppendLog($"[Mod] {message}");
        }

        public async Task ModToggleSwitchAsync(uint entityId, bool isOn)
        {
            try
            {
                if (!await EnsureConnectedAsync())
                {
                    AppendLog($"[Mod] Cannot toggle switch #{entityId}: not connected.");
                    return;
                }
                
                AppendLog($"[Mod] Toggling switch #{entityId} to {(isOn ? "ON" : "OFF")}...");
                await _rust.ToggleSmartSwitchAsync(entityId, isOn);
                
                // Also update local device state in UI if it exists
                var dev = FindDeviceById(_vm.Selected?.Devices, entityId);
                if (dev != null)
                {
                    _suppressToggleHandler = true;
                    dev.IsOn = isOn;
                    _suppressToggleHandler = false;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Mod] Failed to toggle switch #{entityId}: {ex.Message}");
            }
        }

        public System.Collections.ObjectModel.ObservableCollection<SmartDevice>? ModGetDevices()
        {
            return _vm.Selected?.Devices;
        }

        public double GridShiftX { get; set; } = 0.0;
        public double GridShiftY { get; set; } = 0.0;

        public bool IsDragGridMode { get; set; } = false;
        private bool _isDraggingGrid = false;
        private Point _dragGridStartMapPos;
        private double _dragGridStartShiftX;
        private double _dragGridStartShiftY;
        public event Action<double, double>? OnGridOffsetsDragged;

        public void ModRedrawGrid()
        {
            Dispatcher.Invoke(() =>
            {
                RedrawGrid();
            });
        }
    }
}
