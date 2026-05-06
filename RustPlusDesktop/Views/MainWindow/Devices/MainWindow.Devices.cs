using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StorageSnap = RustPlusDesk.Models.StorageSnapshot;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
private void ListDevices_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_vm != null)
        {
            _vm.SelectedDevice = e.NewValue as SmartDevice;
        }
    }

    private Point _dragStartPoint;
    private TreeViewItem? _draggedItemContainer;
    private SmartDevice? _draggedDevice;

    private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        if (sender is TreeViewItem tvi)
        {
            _draggedItemContainer = tvi;
            _draggedDevice = tvi.Header as SmartDevice ?? tvi.DataContext as SmartDevice;
            // Allow event to continue for selection
        }
    }

    private void TreeViewItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedItemContainer != null && _draggedDevice != null)
        {
            Point currentPosition = e.GetPosition(null);
            if (Math.Abs(currentPosition.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPosition.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(_draggedItemContainer, _draggedDevice, DragDropEffects.Move);
            }
        }
    }

    private void TreeViewItem_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SmartDevice)))
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void TreeViewItem_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    private void TreeViewItem_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_draggedDevice == null || !e.Data.GetDataPresent(typeof(SmartDevice)))
            return;

        var targetContainer = sender as TreeViewItem;
        if (targetContainer == null) return;

        var targetDevice = targetContainer.Header as SmartDevice ?? targetContainer.DataContext as SmartDevice;
        if (targetDevice == null || ReferenceEquals(_draggedDevice, targetDevice))
            return;

        if (_vm.Selected?.Devices == null) return;

        Point dropPosition = e.GetPosition(targetContainer);
        double containerHeight = targetContainer.ActualHeight;

        bool dropInto = dropPosition.Y > (containerHeight * 0.25) && dropPosition.Y < (containerHeight * 0.75);

        RemoveDeviceFromHierarchy(_vm.Selected.Devices, _draggedDevice);

        if (dropInto)
        {
            if (!targetDevice.IsGroup)
            {
                var newGroup = new SmartDevice
                {
                    EntityId = GenerateGroupId(),
                    Kind = "Group",
                    IsGroup = true,
                    Alias = "Group " + (_vm.Selected.Devices.Count(d => d.IsGroup) + 1),
                    Children = new System.Collections.ObjectModel.ObservableCollection<SmartDevice>(),
                    IsExpanded = true
                };

                var parentLevel = FindParentCollection(_vm.Selected.Devices, targetDevice) ?? _vm.Selected.Devices;
                int idx = parentLevel.IndexOf(targetDevice);
                if (idx >= 0)
                {
                    parentLevel.Insert(idx, newGroup);
                    parentLevel.Remove(targetDevice);
                }
                else
                {
                    _vm.Selected.Devices.Add(newGroup);
                }

                newGroup.Children.Add(targetDevice);
                newGroup.Children.Add(_draggedDevice);
            }
            else
            {
                if (targetDevice.Children == null)
                    targetDevice.Children = new System.Collections.ObjectModel.ObservableCollection<SmartDevice>();

                targetDevice.Children.Add(_draggedDevice);
                targetDevice.IsExpanded = true;
            }
        }
        else
        {
            bool insertBefore = dropPosition.Y <= (containerHeight * 0.25);
            var parentCol = FindParentCollection(_vm.Selected.Devices, targetDevice);
            if (parentCol != null)
            {
                int index = parentCol.IndexOf(targetDevice);
                if (!insertBefore) index++;
                
                if (index >= parentCol.Count)
                    parentCol.Add(_draggedDevice);
                else
                    parentCol.Insert(index, _draggedDevice);
            }
            else
            {
                _vm.Selected.Devices.Add(_draggedDevice);
            }
        }

        CleanupEmptyGroups(_vm.Selected.Devices);
        _vm.Save();
        _draggedDevice = null;
        _draggedItemContainer = null;
    }

    private bool RemoveDeviceFromHierarchy(System.Collections.ObjectModel.ObservableCollection<SmartDevice> col, SmartDevice toRemove)
    {
        if (col == null) return false;
        if (col.Remove(toRemove)) return true;
        foreach (var dev in col)
        {
            if (dev.IsGroup && dev.Children != null)
            {
                if (RemoveDeviceFromHierarchy(dev.Children, toRemove)) return true;
            }
        }
        return false;
    }

    private System.Collections.ObjectModel.ObservableCollection<SmartDevice>? FindParentCollection(System.Collections.ObjectModel.ObservableCollection<SmartDevice> col, SmartDevice target)
    {
        if (col == null) return null;
        if (col.Contains(target)) return col;
        foreach (var dev in col)
        {
            if (dev.IsGroup && dev.Children != null)
            {
                var found = FindParentCollection(dev.Children, target);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void CleanupEmptyGroups(System.Collections.ObjectModel.ObservableCollection<SmartDevice> col)
    {
        if (col == null) return;
        for (int i = col.Count - 1; i >= 0; i--)
        {
            var dev = col[i];
            if (dev.IsGroup)
            {
                if (dev.Children != null)
                {
                    CleanupEmptyGroups(dev.Children);
                }

                if (dev.Children == null || dev.Children.Count == 0)
                {
                    col.RemoveAt(i);
                }
            }
        }
    }

    private uint GenerateGroupId()
    {
        var rng = new Random();
        uint highBit = 0x80000000;
        uint randomBits = (uint)rng.Next(1, int.MaxValue);
        return highBit | randomBits;
    }

    private async void GroupToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is SmartDevice groupDev && groupDev.IsGroup && groupDev.Children != null)
            {
                AppendLog($"[group/toggle] Toggling group '{groupDev.Alias}'...");
                var switches = GetSwitchesRecursive(groupDev.Children);
                AppendLog($"[group/toggle] Found {switches.Count} switches in group.");
                
                if (!switches.Any()) return;

                if (!await EnsureConnectedAsync()) return;

                bool targetOn = !switches.First().IsOn.GetValueOrDefault(false);
                AppendLog($"[group/toggle] Target state: {(targetOn ? "ON" : "OFF")}");
                
                foreach (var sw in switches)
                {
                    if (sw.IsOn == targetOn) continue;
                    
                    AppendLog($"[group/toggle] Switching #{sw.EntityId} ({sw.Name})...");
                    
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _rust.ToggleSmartSwitchAsync(sw.EntityId, targetOn, cts.Token);
                    
                    await Task.Delay(800); 
                }
                AppendLog("[group/toggle] Group toggle complete.");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[group/toggle] Error: {ex.Message}");
        }
    }

    private List<SmartDevice> GetSwitchesRecursive(IEnumerable<SmartDevice> devices)
    {
        var list = new List<SmartDevice>();
        foreach (var d in devices)
        {
            if (d.IsGroup && d.Children != null)
                list.AddRange(GetSwitchesRecursive(d.Children));
            else if (string.Equals(d.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) || string.Equals(d.Kind, "Smart Switch", StringComparison.OrdinalIgnoreCase))
                list.Add(d);
        }
        return list;
    }

    private SmartDevice? FindDeviceById(IEnumerable<SmartDevice>? devices, uint id)
    {
        if (devices == null) return null;
        foreach (var d in devices)
        {
            if (d.EntityId == id) return d;
            if (d.IsGroup && d.Children != null)
            {
                var found = FindDeviceById(d.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void BtnDeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ListDevices.SelectedItem is SmartDevice dev && dev.IsMissing)
        {
            _vm.Selected?.Devices?.Remove(dev);
            _vm.NotifyDevicesChanged();
            _vm.Save();
            AppendLog($"Device #{dev.EntityId} removed.");
        }
        else
        {
            MessageBox.Show("Only missing devices can be deleted.");
        }
    }

    private void OnSelectAlarmAudioFileClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is SmartDevice dev)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav|All Files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                dev.AudioFilePath = dlg.FileName;
                _vm.Save();
                AppendLog($"Selected audio for #{dev.EntityId}: {dlg.SafeFileName}");
            }
        }
    }

    private void OnResetAlarmAudioFileClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is SmartDevice dev)
        {
            dev.AudioFilePath = null;
            _vm.Save();
            AppendLog($"Reset audio to default for #{dev.EntityId}");
        }
    }

    private System.Windows.Media.MediaPlayer? _alarmPlayer;

    private void PlayAlarmAudio(SmartDevice? dev)
    {
        if (dev == null)
        {
            AppendLog("[audio/debug] Skipping: dev is null");
            return;
        }
        if (!dev.AudioEnabled)
        {
            AppendLog($"[audio/debug] Skipping: AudioEnabled is false for #{dev.EntityId}");
            return;
        }
        
        string audioFile = "";
        
        if (!string.IsNullOrWhiteSpace(dev.AudioFilePath))
        {
            audioFile = dev.AudioFilePath!;
        }
        else
        {
            audioFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icons", "rust-c4.mp3");
        }

        if (System.IO.File.Exists(audioFile))
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (_alarmPlayer == null)
                    {
                        _alarmPlayer = new System.Windows.Media.MediaPlayer();
                    }
                    AppendLog($"[audio] Playing: {audioFile}");
                    _alarmPlayer.Open(new Uri(audioFile, UriKind.Absolute));
                    _alarmPlayer.Volume = 1.0;
                    _alarmPlayer.Play();
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[audio] Error playing alarm audio: {ex.Message}");
            }
        }
        else
        {
            AppendLog($"[audio] File not found: {audioFile}");
        }
    }

private async void DeviceToggle_Click(object sender, RoutedEventArgs e)
    {
        lock (_toggleBusy)
        {
            foreach (var id in _toggleBusy.Where(kv => DateTime.UtcNow - kv.Value > ToggleBusyTTL).Select(kv => kv.Key).ToList())
                _toggleBusy.Remove(id);
        }
        if ((sender as ToggleButton)?.DataContext is not SmartDevice d) return;
        if (!await EnsureConnectedAsync()) { ((ToggleButton)sender).IsChecked = d.IsOn; return; }

        var desired = ((ToggleButton)sender).IsChecked == true;
        try
        {
            await _rust.ToggleSmartSwitchAsync(d.EntityId, desired);
            // Verify holst du dir ja schon – zur Sicherheit:
            d.IsOn = desired;
        }
        catch (Exception ex)
        {
            AppendLog("Control error: " + ex.Message);
            ((ToggleButton)sender).IsChecked = d.IsOn; // UI zurücksetzen
        }
    }


    private bool _suppressToggleHandler;
    private async void DeviceToggle_Checked(object sender, RoutedEventArgs e)
        => await HandleDeviceToggleAsync(sender, true);

    private async void DeviceToggle_Unchecked(object sender, RoutedEventArgs e)
        => await HandleDeviceToggleAsync(sender, false);

    // 1) Toggle-Handler bleibt so – ohne Refresh-Aufruf
    private static bool LooksLikeNotConnected(Exception ex)
    {
        var s = ex.Message?.ToLowerInvariant() ?? "";
        return ex is WebSocketException
            || ex is IOException
            || s.Contains("nicht verbunden")
            || s.Contains("not connected")
            || s.Contains("websocket")
            || s.Contains("closed")
            || s.Contains("aborted");
    }

    private async Task HandleDeviceToggleAsync(object sender, bool on)
    {
        if (_suppressToggleHandler) return;

        if ((sender as FrameworkElement)?.DataContext is not SmartDevice dev) return;
        if (!string.Equals(dev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase)) return;

        // Lock erst NACH erfolgreichem Connect setzen? → dein Flow kann bleiben,
        // ABER ganz wichtig: Timeout um das Toggle, damit der Task nicht hängt.
        if (!TryMarkToggleBusy(dev.EntityId))
        {
            AppendLog($"(skip) Toggle #{dev.EntityId} already in progress");
            return;
        }

        try
        {
            if (!await EnsureConnectedAsync()) return;

            if ((DateTime.UtcNow - _lastPairingPingAt).TotalMilliseconds < 1200)
            {
                AppendLog("Pairing ping just arrived – delaying toggle 1.2s …");
                await Task.Delay(1200);
            }

            AppendLog($"Sending {(on ? "ON" : "OFF")} to #{dev.EntityId} …");

            try
            {
                // *** WICHTIG: immer mit Timeout aufrufen ***
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await _rust.ToggleSmartSwitchAsync(dev.EntityId, on, cts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("Toggle timeout (8s).");
            }
            catch (Exception ex)
            {
                AppendLog($"{(on ? "ON" : "OFF")} Error: " + ex.Message);

                // Optional: einmaliger Reconnect-Retry bei „nicht verbunden“
                if (LooksLikeNotConnected(ex) && await EnsureConnectedAsync())
                {
                    using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    try { await _rust.ToggleSmartSwitchAsync(dev.EntityId, on, cts2.Token); }
                    catch (Exception ex2) { AppendLog("Retry failed: " + ex2.Message); }
                }
            }
            finally
            {
                await RefreshDeviceStateAsync(dev);
            }
        }
        finally
        {
            UnmarkToggleBusy(dev.EntityId); // <<< wird garantiert ausgeführt
        }
    }

    private readonly Dictionary<uint, StorageSnap> _storageCache = new();

    private void CacheStorage(uint id, StorageSnap? snap)
    {
        if (snap == null) return;
        _storageCache[id] = snap;
    }

    private bool TryGetCachedStorage(uint id, out StorageSnap? snap)
        => _storageCache.TryGetValue(id, out snap);
    // Ein Gerät *generisch* neu einlesen (wie der Info-Button).
    // - Für SmartSwitch: schneller Pfad über GetSmartSwitchStateAsync
    // - Für alle anderen (oder Fallback): ProbeEntityAsync (setzt auch Kind/IsMissing)
    private async Task RefreshDeviceStateAsync(SmartDevice dev, bool log = true, bool forcePull = false)
    {
        if (_rust is not RustPlusClientReal real) return;

        if (RustPlusClientReal.IsStorageDevice(dev))
        {
            try
            {
                // (1) Cache → UI
                if (real.TryGetCachedStorage(dev.EntityId, out var cached))
                {
                    Dispatcher.Invoke(() =>
                    {
                        var uiSnap = new StorageSnapshot
                        {
                            UpkeepSeconds = cached.UpkeepSeconds,
                            IsToolCupboard = cached.IsToolCupboard,
                            SnapshotUtc = cached.SnapshotUtc
                        };
                        foreach (var it in cached.Items)
                            uiSnap.Items.Add(it);

                        dev.Storage = uiSnap;
                        dev.IsMissing = false;
                    });
                }
                else
                {
                    dev.Storage ??= new StorageSnapshot();
                }

                // (2) einmalig subscriben
                try
                {
                    await real.EnsureSubOnceAsync(dev.EntityId);
                }
                catch (Exception subEx)
                {
                    if (log) AppendLog($"[stor/sub+poke] #{dev.EntityId} failed: {subEx.Message}");
                }

                // (3) bei forcePull denselben Weg wie Refresh nutzen
                if (forcePull)
                {
                    try
                    {
                        // wir brauchen das Ergebnis nicht – wichtig ist der Side-Effekt:
                        // DecodeEntityInfo / Events aktualisieren den Storage-Cache
                        await _rust.ProbeEntityAsync(dev.EntityId);
                       // if (log)
                        //    AppendLog($"[stor/poll] probe #{dev.EntityId} queued");
                    }
                    catch (Exception pullEx)
                    {
                        if (log) AppendLog($"[stor/poll] #{dev.EntityId} probe EX: {pullEx.Message}");
                    }
                }
                else
                {
                    // optionaler Fallback nur, wenn noch kein Cache existiert
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        if (real.TryGetCachedStorage(dev.EntityId, out _))
                            return;

                        try
                        {
                            // hier kannst du dein GetStorageMonitorAsync ruhig behalten,
                            // falls es für den Erst-Snapshot funktioniert
                            await real.GetStorageMonitorAsync(dev.EntityId);
                            if (log)
                                AppendLog($"[stor/pull] #{dev.EntityId} queued fallback pull");
                        }
                        catch (Exception ex2)
                        {
                            AppendLog($"[stor/pull] #{dev.EntityId} fallback EX: {ex2.Message}");
                        }
                    });
                }

                return;
            }
            catch (Exception ex)
            {
                dev.Storage ??= new StorageSnapshot();
                if (log) AppendLog($"[stor/refresh] #{dev.EntityId} EX: {ex.Message}");
                return;
            }
        }

        // ===== Generisch (SmartAlarm & Co.) =====
        try
        {
            var r = await _rust.ProbeEntityAsync(dev.EntityId);

            // Kind nur setzen, NIE SmartAlarm überschreiben
            if (!string.IsNullOrWhiteSpace(r.Kind) &&
                !string.Equals(dev.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
            {
                dev.Kind = r.Kind;
            }

            dev.IsMissing = !r.Exists;

            _suppressToggleHandler = true;
            if ((dev.Kind ?? r.Kind)?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Alarmstatus nicht aus Probe übernehmen
                dev.IsOn = false;
            }
            else
            {
                dev.IsOn = r.IsOn;
            }
            _suppressToggleHandler = false;

            if (log)
            {
                if (!r.Exists)
                    AppendLog($"#{dev.EntityId}: not reachable / demoted");
                else
                    AppendLog($"State #{dev.EntityId}: {(r.IsOn is bool b ? (b ? "ON" : "OFF") : "–")} ({r.Kind ?? "?"})");
            }
            _vm?.NotifyDevicesChanged();
        }
        catch (Exception ex)
        {
            if (log) AppendLog("Probe-Error: " + ex.Message);
        }
    }

private async void BtnDeviceRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllDevicesStatusAsync();
    }

    private async void BtnDeviceInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedDevice is null) { AppendLog("No Device Selected."); return; }
        if (!await EnsureConnectedAsync()) return;

        try
        {
            await RefreshDeviceStateAsync(_vm.SelectedDevice); // <-- einheitlicher Pfad
        }
        catch (Exception ex)
        {
            AppendLog("Info-Error: " + ex.Message);
        }
    }

    private void RehydrateCamerasFromStorageInto(ServerProfile current)
    {
        try
        {
            var all = StorageService.LoadProfiles();
            var saved = all.FirstOrDefault(p =>
                p.Host.Equals(current.Host, StringComparison.OrdinalIgnoreCase) &&
                p.Port == current.Port &&
                p.SteamId64 == current.SteamId64);

            current.CameraIds ??= new ObservableCollection<string>();

            if (saved?.CameraIds is { Count: > 0 })
            {
                foreach (var id in saved.CameraIds)
                    if (!current.CameraIds.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)))
                        current.CameraIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            AppendLog("RehydrateCams-Error: " + ex.Message);
        }
    }

    private void RehydrateDevicesFromStorageInto(ServerProfile current)
    {
        try
        {
            var all = StorageService.LoadProfiles();
            var saved = all.FirstOrDefault(p =>
                p.Host.Equals(current.Host, StringComparison.OrdinalIgnoreCase) &&
                p.Port == current.Port &&
                p.SteamId64 == current.SteamId64);

            current.Devices ??= new();

            if (saved?.Devices is { Count: > 0 })
            {
                // 1) upsert
                foreach (var s in saved.Devices)
                {
                    var ex = FindDeviceById(current.Devices, s.EntityId);
                    if (ex == null)
                    {
                        current.Devices.Add(s);
                    }
                    else
                    {
                        ex.Name = s.Name;
                        ex.Kind = s.Kind;
                        ex.IsOn = s.IsOn;
                        // ⚠️ SmartAlarm nie als "true" aus Storage hochholen
                        if (string.Equals(ex.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
                            ex.IsOn = false;
                        ex.IsMissing = s.IsMissing;
                        ex.Alias = s.Alias;
                    }
                }

                // 2) optional: entfernen, was im Storage nicht mehr existiert
                // for (int i = current.Devices.Count - 1; i >= 0; i--)
                //     if (!saved.Devices.Any(d => d.EntityId == current.Devices[i].EntityId))
                //         current.Devices.RemoveAt(i);
            }

            _vm.NotifyDevicesChanged();
        }
        catch (Exception ex)
        {
            AppendLog("Rehydrate-Error: " + ex.Message);
        }
    }

private void Device_Rename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SmartDevice dev) return;

        var title = dev.IsGroup ? "Rename Group" : "Rename Device";
        var promptText = dev.IsGroup ? "New name for group:" : $"New name for #{dev.EntityId}:";
        var preset = string.IsNullOrWhiteSpace(dev.Alias) ? (dev.Name ?? "") : dev.Alias!;
        var input = PromptText(this, title, promptText, preset);

        if (input == null) return;                   // Abgebrochen
        dev.Alias = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        _vm.Save();                                  // Profile inkl. Alias persistieren
    }
    private const double MIN_S = 1.0;   // nicht kleiner als "fit"
    private const double MAX_S = 8.0;
    // Mini-Prompt, keine zusätzlichen XAML-Dateien nötig
    private static string? PromptText(Window owner, string title, string message, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false
        };

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tbMsg = new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 6) };
        var box = new TextBox { Text = initial, MinWidth = 300 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 100, IsCancel = true };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        Grid.SetRow(tbMsg, 0); grid.Children.Add(tbMsg);
        Grid.SetRow(box, 1); grid.Children.Add(box);
        Grid.SetRow(buttons, 2); grid.Children.Add(buttons);

        string? result = null;
        ok.Click += (_, __) => { result = box.Text; win.DialogResult = true; };
        win.Content = grid;

        return win.ShowDialog() == true ? result : null;
    }

public List<ExportedDeviceDto> Devices { get; set; } = new();

    private async void BtnDevicesExport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            AppendLog("[dev/export] No server selected.");
            return;
        }

        if (!await EnsureConnectedAsync())
            return;

        try
        {
            var count = await UploadDevicesSnapshotForCurrentServerAsync();
            AppendLog($"[dev/export] Exported {count} devices for server '{_vm.Selected.Name}'.");
            MessageBox.Show($"Exported {count} devices to your team share.", "Device Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog("[dev/export] Error: " + ex.Message);
            MessageBox.Show("Device export failed:\n" + ex.Message, "Device Export",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<int> UploadDevicesSnapshotForCurrentServerAsync()
    {
        var profile = _vm.Selected;
        if (profile?.Devices == null || profile.Devices.Count == 0)
            throw new InvalidOperationException("No devices in current profile.");

        // 1) aktuelles Overlay-JSON aufbauen (deine bestehende Methode)
        var data = BuildCurrentOverlaySaveDataForMe(); // <- nutzt du bereits für Map-Overlay

        // 2) Devices-Liste füllen (Rekursiv)
        data.Devices.Clear();
        foreach (var d in profile.Devices)
        {
            data.Devices.Add(MapDeviceToDto(d));
        }

        data.LastUpdatedUnix = UnixNow(); // damit remote/locally „neuer“ ist

        // 3) JSON serialisieren, Größenlimit prüfen, Base64 wie beim Overlay
        var json = System.Text.Json.JsonSerializer.Serialize(
            data,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });

        var rawBytes = Encoding.UTF8.GetBytes(json);
        if (rawBytes.Length > OVERLAY_MAX_BYTES)
            throw new InvalidOperationException("Device export is too big (>350KB).");

        var overlayB64 = Convert.ToBase64String(rawBytes);

        var serverKey = GetServerKey();
        var ts = UnixNow().ToString();
        var sigInput = _mySteamId.ToString() + "|" + serverKey + "|" + ts + "|" + overlayB64;
        var sig = HmacSha256Hex(OVERLAY_SYNC_SECRET_HEX, sigInput);

        var payloadObj = new
        {
            steamId = _mySteamId.ToString(),
            serverKey = serverKey,
            ts = ts,
            overlayJsonB64 = overlayB64,
            sig = sig
        };

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadObj);
        var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using (var http = new HttpClient())
        {
            var url = OVERLAY_SYNC_BASEURL + "/upload";
            var resp = await http.PostAsync(url, content);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException("Upload failed: HTTP " + (int)resp.StatusCode);
        }

        // 4) optional auch lokal die Datei aktualisieren, damit Import sofort darauf zugreifen kann
        var localPath = GetOverlayJsonPathForPlayerServer(_mySteamId);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(localPath)!);
        File.WriteAllText(localPath, json);

        return data.Devices.Count;
    }

    private ExportedDeviceDto MapDeviceToDto(SmartDevice d)
    {
        var dto = new ExportedDeviceDto
        {
            EntityId = d.EntityId,
            Kind = d.Kind,
            Name = d.Name,
            Alias = d.Alias,
            IsGroup = d.IsGroup
        };

        if (d.IsGroup && d.Children != null && d.Children.Count > 0)
        {
            dto.Children = new List<ExportedDeviceDto>();
            foreach (var child in d.Children)
            {
                dto.Children.Add(MapDeviceToDto(child));
            }
        }

        return dto;
    }

    private async void BtnDevicesImport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null)
        {
            AppendLog("[dev/import] No server selected.");
            return;
        }

        if (!await EnsureConnectedAsync())
            return;

        try
        {
            // 1) Von allen Team-Mitgliedern den Overlay-JSON aktualisieren
            var fetchTasks = TeamMembers
                .Where(tm => tm.SteamId != 0)
                .Select(tm => TryFetchAndUpdateOverlayAsync(tm.SteamId));
            await Task.WhenAll(fetchTasks);

            // 2) Import-Kandidaten aus lokalen Overlay-Dateien sammeln
            var items = new List<DeviceImportItem>();

            foreach (var tm in TeamMembers)
            {
                if (tm.SteamId == 0) continue;
                var path = GetOverlayJsonPathForPlayerServer(tm.SteamId);
                if (!File.Exists(path)) continue;

                OverlaySaveData? data = null;
                try
                {
                    var json = File.ReadAllText(path);
                    data = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
                }
                catch (Exception ex)
                {
                    AppendLog($"[dev/import] Can't parse overlay for {tm.SteamId}: {ex.Message}");
                    continue;
                }

                if (data?.Devices == null || data.Devices.Count == 0)
                    continue;

                foreach (var d in data.Devices)
                {
                    AddDeviceToImportItems(items, d, tm);
                }
            }

            if (items.Count == 0)
            {
                MessageBox.Show("No device exports found for your team / server.",
                    "Device Import", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new DeviceImportWindow(
    items,
    id => _rust.ProbeEntityAsync(id));

            dlg.Owner = this;
            var ok = dlg.ShowDialog() == true;
            if (!ok) return;

            var toImport = dlg.SelectedItems;
            if (toImport == null || toImport.Count == 0) return;

            // 3) Ausgewählte Devices ins aktuelle Profile übernehmen
            foreach (var it in toImport)
            {
                if (it.OriginalDto != null)
                {
                    // Rekursiver Import via DTO
                    if (_vm.Selected.Devices.Any(d => d.EntityId == it.OriginalDto.EntityId))
                        continue;

                    var dev = MapDtoToDevice(it.OriginalDto);
                    _vm.Selected.Devices.Add(dev);
                }
                else
                {
                    // Fallback (sollte nicht mehr passieren)
                    if (_vm.Selected.Devices.Any(d => d.EntityId == it.EntityId))
                        continue;

                    var dev = new SmartDevice
                    {
                        EntityId = it.EntityId,
                        Kind = it.Kind,
                        Name = it.Name,
                        Alias = it.Alias,
                        IsMissing = true
                    };
                    _vm.Selected.Devices.Add(dev);
                }
            }

            _vm.NotifyDevicesChanged();
            _vm.Save();

            AppendLog($"[dev/import] Imported {toImport.Count} devices.");

            // 4) Direkt anschließender Status-Refresh
            await RefreshAllDevicesStatusAsync();
        }
        catch (Exception ex)
        {
            AppendLog("[dev/import] Error: " + ex.Message);
            MessageBox.Show("Device import failed:\n" + ex.Message,
                "Device Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshAllDevicesStatusAsync()
    {
        if (_vm.Selected is null)
        {
            AppendLog("No Server Selected.");
            return;
        }

        if (!await EnsureConnectedAsync())
            return;

        var list = _vm.Selected.Devices;
        if (list == null || list.Count == 0)
        {
            AppendLog("No Devices Available.");
            return;
        }

        AppendLog("Updating Device Status…");
        foreach (var d in list)
        {
            await RefreshDeviceRecursiveAsync(d);
        }

        _vm.Save();
        AppendLog("Refresh completed.");
    }

    private async Task RefreshDeviceRecursiveAsync(SmartDevice d)
    {
        try
        {
            if (!d.IsGroup)
            {
                var r = await _rust.ProbeEntityAsync(d.EntityId);

                d.Kind = r.Kind ?? d.Kind;
                d.IsMissing = !r.Exists;
                if ((d.Kind ?? r.Kind)?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) == true)
                {
                    d.IsOn = false; // Alarm → null als „aus“ deuten
                }
                else
                {
                    d.IsOn = r.IsOn;
                }

                if (!r.Exists)
                    AppendLog($"#{d.EntityId}: not reachable / removed");
                else
                    AppendLog($"#{d.EntityId} ({d.Kind ?? "?"}): {(r.IsOn is bool b ? (b ? "ON" : "OFF") : "–")}");
            }
            else
            {
                // Gruppen haben keinen direkten Status, aber wir refreshen Kinder
                if (d.Children != null)
                {
                    foreach (var child in d.Children)
                    {
                        await RefreshDeviceRecursiveAsync(child);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            d.IsMissing = true;
            if (!d.IsGroup)
                AppendLog($"#{d.EntityId}: Status Request Failed → {ex.Message}");
        }
    }

    private void AddDeviceToImportItems(List<DeviceImportItem> items, ExportedDeviceDto d, TeamMemberVM tm)
    {
        // Wir fügen nur Top-Level Items hinzu, die dann aber den OriginalDto mitschleifen
        bool already = _vm.Selected.Devices?.Any(x => x.EntityId == d.EntityId) == true;

        var item = new DeviceImportItem
        {
            OwnerSteamId = tm.SteamId,
            OwnerName = tm.Name ?? tm.SteamId.ToString(),
            EntityId = d.EntityId,
            Kind = d.Kind,
            Name = d.Name,
            Alias = d.Alias,
            AlreadyPresent = already,
            IsSelected = !already,
            ExistsState = already ? "local" : "?",
            ServerName = _vm.Selected.Name,
            OriginalDto = d // <- Hier speichern wir das volle DTO inklusive Children!
        };
        items.Add(item);
    }

    private SmartDevice MapDtoToDevice(ExportedDeviceDto dto)
    {
        var dev = new SmartDevice
        {
            EntityId = dto.EntityId,
            Kind = dto.Kind,
            Name = dto.Name,
            Alias = dto.Alias,
            IsGroup = dto.IsGroup,
            IsMissing = true
        };

        if (dto.Children != null && dto.Children.Count > 0)
        {
            foreach (var childDto in dto.Children)
            {
                dev.Children.Add(MapDtoToDevice(childDto));
            }
        }

        return dev;
    }

private void DeviceRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem lbi && lbi.DataContext is SmartDevice sd)
        {
            // Nur für StorageMonitor reagieren – SmartSwitches etc. bleiben unberührt
            if (string.Equals(sd.Kind, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
            {
                sd.IsExpanded = !sd.IsExpanded;
              //  AppendLog($"[ui] expand #{sd.EntityId} -> {sd.IsExpanded}");
                e.Handled = true; // verhindert, dass noch andere Handler „verbrauchen“
            }
        }
    }
    private async void StorageRow_Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SmartDevice sd)
        {
            sd.IsExpanded = !sd.IsExpanded;
            AppendLog($"[ui] expand #{sd.EntityId} -> {sd.IsExpanded}");

            if (sd.IsExpanded)
                await RefreshDeviceStateAsync(sd, log: true);
        }
        e.Handled = true;
    }

    private void Pill_PreviewMouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void Pill_PreviewMouseUp(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void StorageChip_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Button b) return;
        if (b.DataContext is not SmartDevice dev || dev.Storage == null || dev.Storage.ItemsCount == 0)
            return; // kein Flyout bei leeren Daten

        var cm = b.ContextMenu;
        if (cm == null) return;
        cm.PlacementTarget = b;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void CtxClosedReleaseCapture(object? sender, RoutedEventArgs e)
    {
        // 3) Falls die Maus noch „gecaptured“ ist, freigeben
        try { Mouse.Capture(null); } catch { }
    }




    public sealed class SecondsToPrettyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "";
            var sec = System.Convert.ToInt64(value);
            if (sec <= 0) return "Upkeep: –";
            var ts = TimeSpan.FromSeconds(sec);
            if (ts.TotalDays >= 1)
                return $" {(int)ts.TotalDays}d {ts.Hours}h";
            if (ts.TotalHours >= 1)
                return $"Upkeep: {(int)ts.TotalHours}h {ts.Minutes}m";
            return $"Upkeep: {ts.Minutes}m {ts.Seconds}s";
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    private async Task PrimeDeviceKindsAsync()
    {
        if (_vm?.Selected?.Devices == null || _vm.Selected.Devices.Count == 0) return;

        AppendLog("[prime] probing device kinds …");
        foreach (var d in _vm.Selected.Devices)
        {
            try
            {
                var r = await _rust.ProbeEntityAsync(d.EntityId);
                if (!string.IsNullOrWhiteSpace(r.Kind))
                    d.Kind = r.Kind;
                d.IsMissing = !r.Exists;
                AppendLog($"[prime] #{d.EntityId} kind={d.Kind ?? "?"} exists={r.Exists}");
            }
            catch (Exception ex)
            {
                AppendLog($"[prime] #{d.EntityId} err: {ex.Message}");
            }
            // Throttle requests to avoid flooding/disconnects
            await Task.Delay(250);
        }
        _vm?.NotifyDevicesChanged();
    }

// kommt aus RustPlusClientReal.StorageSnapshotReceived
    private void OnStorageSnapshot(uint entityId, StorageSnapshot snap)
{
    Dispatcher.Invoke(() =>
    {
        SmartDevice? dev = null;

        // 1) Aktuelle Sicht (gefilterte Liste)
        if (_vm?.CurrentDevices != null)
            dev = FindDeviceById(_vm.CurrentDevices, entityId);

        // 2) Fallback: alle Devices des ausgewählten Servers
        if (dev == null)
            dev = FindDeviceById(_vm?.Selected?.Devices, entityId);

        if (dev == null)
            return;

        dev.IsMissing = false;

        var uiSnap = new StorageSnapshot
        {
            UpkeepSeconds  = snap.UpkeepSeconds,
            IsToolCupboard = snap.IsToolCupboard,
            SnapshotUtc    = DateTime.UtcNow
        };
        foreach (var it in snap.Items)
            uiSnap.Items.Add(it);

        dev.Storage = uiSnap;

        if (!dev.IsExpanded)
            dev.IsExpanded = true;
    });
}

   
    

    private void StorPill_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        // Tooltip nur zeigen, wenn Daten da sind
        if (sender is FrameworkElement fe && fe.DataContext is SmartDevice dev)
        {
            if (dev.Storage == null || dev.Storage.ItemsCount == 0)
            {
                e.Handled = true;
                return;
            }

            // ScrollViewer im ToolTip suchen und an der Pille "cachen"
            if (fe.ToolTip is ToolTip tip)
            {
                // Suche nur einmal
                if (fe.Tag is not ScrollViewer)
                {
                    var sv = FindDescendant<ScrollViewer>(tip);
                    fe.Tag = sv; // kann null sein – dann versuchen wir's später nochmal
                }
            }
        }
    }

    private void StorPill_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            // ScrollViewer aus dem Tag holen (oder on-the-fly suchen)
            var sv = fe.Tag as ScrollViewer;
            if (sv == null && fe.ToolTip is ToolTip tip)
            {
                sv = FindDescendant<ScrollViewer>(tip);
                fe.Tag = sv;
            }

            if (sv != null)
            {
                // Delta ist typischerweise ±120 – wir scrollen zeilenweise
                if (e.Delta < 0) sv.LineDown(); else sv.LineUp();
                e.Handled = true; // verhindert, dass das Radereignis nach hinten "durchfällt"
            }
        }
    }

    /// <summary>Findet das erste Descendant-Element vom Typ T im VisualTree.</summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) return null;
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var r = FindDescendant<T>(child);
            if (r != null) return r;
        }
        return null;
    }
}
