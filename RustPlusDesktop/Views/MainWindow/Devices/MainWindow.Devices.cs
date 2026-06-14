using RustPlusDesk.Models;
using RustPlusDesk.Services.Data;
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
using RustPlusDesk.Services;
using StorageSnap = RustPlusDesk.Models.StorageSnapshot;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // =========================================================
    // API Backpressure Tracker
    // Incremented on consecutive poll failures, decremented on success.
    // Low-priority polls (shops, storage) skip their tick while active.
    // =========================================================
    private int _apiConsecutiveTimeouts = 0;
    private const int ApiPressureThreshold = 5;
    private bool IsApiUnderPressure => _apiConsecutiveTimeouts >= ApiPressureThreshold;

    private void OnApiPollSuccess()
    {
        if (_apiConsecutiveTimeouts > 0)
        {
            _apiConsecutiveTimeouts = 0;
            AppendLog("[pressure] API pressure relieved.");
        }
    }

    private void OnApiPollTimeout()
    {
        _apiConsecutiveTimeouts = Math.Min(10, _apiConsecutiveTimeouts + 1);
        if (_apiConsecutiveTimeouts == ApiPressureThreshold)
            AppendLog("[pressure] API under pressure – low-priority polls paused.");
    }
    // =========================================================

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
        if (e.OriginalSource is DependencyObject depObj)
        {
            var parentBorder = FindParentBorderByName(depObj, "IconBorder");
            if (parentBorder != null)
            {
                // Let the click pass to the IconBorder's handler but do not initiate drag/drop
                return;
            }
        }

        _dragStartPoint = e.GetPosition(null);
        if (sender is TreeViewItem tvi)
        {
            _draggedItemContainer = tvi;
            _draggedDevice = tvi.Header as SmartDevice ?? tvi.DataContext as SmartDevice;
            // Allow event to continue for selection
        }
    }

    private Border? FindParentBorderByName(DependencyObject child, string name)
    {
        if (child == null) return null;
        if (child is Border border && border.Name == name) return border;
        try
        {
            var parent = VisualTreeHelper.GetParent(child);
            return parent != null ? FindParentBorderByName(parent, name) : null;
        }
        catch
        {
            return null;
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

        if (IsDescendant(_draggedDevice, targetDevice))
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
        _vm.Selected.NotifySmartSwitchesChanged();
        _vm.Save();
        _draggedDevice = null;
        _draggedItemContainer = null;
    }

    private bool IsDescendant(SmartDevice potentialParent, SmartDevice potentialChild)
    {
        if (potentialParent.Children == null) return false;
        if (potentialParent.Children.Contains(potentialChild)) return true;
        foreach (var child in potentialParent.Children)
        {
            if (child.IsGroup && IsDescendant(child, potentialChild))
                return true;
        }
        return false;
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

    private async void BtnDeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ListDevices.SelectedItem is SmartDevice dev)
        {
            if (_vm.Selected?.Devices != null)
            {
                RemoveDeviceAndChildrenFromHotkeys(dev);
                RemoveDeviceFromHierarchy(_vm.Selected.Devices, dev);
                _vm.Selected.NotifyFlatDevicesChanged();
                _vm.NotifyDevicesChanged();
                _vm.Save();
                AppendLog($"Device #{dev.EntityId} removed.");

                // Sync the local disk overlay JSON as well
                SaveOwnOverlayToJson();

                // Immediately upload new state to Cloud
                if (TrackingService.CloudSyncEnabled && RustPlusDesk.Services.Auth.SupabaseAuthManager.Client != null)
                {
                    try
                    {
                        await UploadDevicesSnapshotForCurrentServerAsync(explicitWipe: true);
                        AppendLog("[dev/sync] Pushed updated device list to cloud.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog("[dev/sync] Error: " + ex.Message);
                    }
                }
            }
        }
    }

    private void RemoveDeviceAndChildrenFromHotkeys(SmartDevice dev)
    {
        var map = MapForCurrentServer();
        if (map == null) return;
        
        bool changed = false;

        void RemoveRecursive(SmartDevice d)
        {
            foreach (var list in map.Values)
            {
                if (list.Remove(d.EntityId)) changed = true;
            }
            if (d.Children != null)
            {
                foreach (var child in d.Children) RemoveRecursive(child);
            }
        }

        RemoveRecursive(dev);
        if (changed) SaveHotkeys();
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
    private System.Windows.Media.MediaPlayer? _loopPlayer;
    private bool _isLooping;

    public void StopLoopPlayer()
    {
        Dispatcher.Invoke(() =>
        {
            if (_loopPlayer != null && _isLooping)
            {
                _isLooping = false;
                _loopPlayer.Stop();
                AppendLog($"[audio] Stopped looping audio.");
            }
        });
    }

    public void StopAlarmPlayer()
    {
        Dispatcher.Invoke(() =>
        {
            if (_alarmPlayer != null)
            {
                _alarmPlayer.Stop();
                AppendLog($"[audio] Stopped standard alarm audio.");
            }
        });
    }

    private void PlayAlarmAudio(SmartDevice? dev)
    {
        // Wenn ein Gerät erkannt wurde, prüfen wir seine individuellen Audio-Einstellungen.
        // Wenn kein Gerät erkannt wurde (generischer Alarm), spielen wir den Standard-Sound ab.
        if (dev != null && !dev.AudioEnabled) return;

        try
        {
            string baseDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            string audioFile = "";

            if (dev != null && !string.IsNullOrWhiteSpace(dev.AudioFilePath))
            {
                audioFile = dev.AudioFilePath!;
            }
            else
            {
                // Standard-Pfad unter Assets
                audioFile = System.IO.Path.Combine(baseDir, "Assets", "icons", "rust-c4.mp3");
                if (!System.IO.File.Exists(audioFile))
                {
                    // Fallback für alte Ordnerstruktur
                    audioFile = System.IO.Path.Combine(baseDir, "icons", "rust-c4.mp3");
                }
            }

            if (System.IO.File.Exists(audioFile))
            {
                var fullPath = System.IO.Path.GetFullPath(audioFile);
                Dispatcher.Invoke(() =>
                {
                    bool useLoopPlayer = dev != null && dev.AudioLoopEnabled;

                    if (useLoopPlayer)
                    {
                        if (_loopPlayer == null)
                        {
                            _loopPlayer = new System.Windows.Media.MediaPlayer();
                            _loopPlayer.MediaFailed += (s, e) => AppendLog($"[audio] Loop Media Failed: {e.ErrorException?.Message}");
                            _loopPlayer.MediaEnded += (s, e) => {
                                if (_isLooping && _loopPlayer != null)
                                {
                                    _loopPlayer.Position = TimeSpan.Zero;
                                    _loopPlayer.Play();
                                }
                            };
                        }

                        _loopPlayer.Stop(); // Stoppen, falls noch etwas läuft
                        _loopPlayer.Open(new Uri(fullPath, UriKind.Absolute));
                        _loopPlayer.Volume = 1.0;
                        _isLooping = true;
                        _loopPlayer.Play(); // Starten
                        
                        AppendLog($"[audio] Looping: {fullPath}");
                    }
                    else
                    {
                        if (_alarmPlayer == null)
                        {
                            _alarmPlayer = new System.Windows.Media.MediaPlayer();
                            _alarmPlayer.MediaFailed += (s, e) => AppendLog($"[audio] Media Failed: {e.ErrorException?.Message}");
                        }

                        _alarmPlayer.Stop(); // Stoppen, falls noch etwas läuft
                        _alarmPlayer.Open(new Uri(fullPath, UriKind.Absolute));
                        _alarmPlayer.Volume = 1.0;
                        _alarmPlayer.Play(); // Starten
                        
                        AppendLog($"[audio] Playing: {fullPath}");
                    }
                });
            }
            else
            {
                AppendLog($"[audio] File not found: {audioFile}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[audio] Error playing alarm audio: {ex.Message}");
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

    private async Task HandleDeviceToggleAsync(object sender, bool on, bool ignoreGlobalBusy = false)
    {
        if (_suppressToggleHandler) return;

        if (_logicEngineRunningAction)
        {
            AppendLog("[LogicEngine] Manual toggle blocked: Logic Engine is executing an action.");
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not SmartDevice dev) return;
        if (!string.Equals(dev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase)) return;

        // Lock erst NACH erfolgreichem Connect setzen? → dein Flow kann bleiben,
        // ABER ganz wichtig: Timeout um das Toggle, damit der Task nicht hängt.
        if (!ignoreGlobalBusy && _globalToggleBusy) return;
        if (!TryMarkToggleBusy(dev.EntityId))
        {
            AppendLog($"(skip) Toggle #{dev.EntityId} already in progress");
            return;
        }

        if (!ignoreGlobalBusy)
        {
            _globalToggleBusy = true;
        }

        dev.IsToggleBusy = true;
        int attempts = 0;
        bool success = false;
        try
        {
            if (!await EnsureConnectedAsync()) return;

            if ((DateTime.UtcNow - _lastPairingPingAt).TotalMilliseconds < 1200)
            {
                AppendLog("Pairing ping just arrived – delaying toggle 1.2s …");
                await Task.Delay(1200);
            }

            AppendLog($"Sending {(on ? "ON" : "OFF")} to #{dev.EntityId} …");

            const int maxAttempts = 3;

            while (attempts < maxAttempts && !success)
            {
                attempts++;
                try
                {
                    // Shorter timeout (5s) for toggling to avoid clogging the queue
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _rust.ToggleSmartSwitchAsync(dev.EntityId, on, cts.Token);
                    success = true;
                }
                catch (Exception ex)
                {
                    if (attempts >= maxAttempts)
                    {
                        AppendLog($"Device #{dev.EntityId}: Switching failed after {attempts} attempts – {ex.Message}");
                        dev.IsMissing = true;
                        
                        // Health check: Trigger a direct probe to confirm if it's really missing
                        _ = Task.Run(async () => await RefreshDeviceStateAsync(dev));
                    }
                    else
                    {
                        // More breathing room between retries (2s)
                        AppendLog($"Device #{dev.EntityId}: Toggle attempt {attempts} failed, retrying in 2s …");
                        await Task.Delay(2000);
                        if (!await EnsureConnectedAsync()) break;
                    }
                }
            }
        }
        finally
        {
            // REMOVED: Aggressive RefreshDeviceStateAsync probe.
            // If success is false, dev.IsMissing was already set in the catch block.
            // We don't want to flood the API with more probes if the toggle already failed.
            
            UnmarkToggleBusy(dev.EntityId);
            dev.IsToggleBusy = false;
            
            // Release global lock after a small cooldown to prevent spamming different switches
            if (!ignoreGlobalBusy)
            {
                _ = Task.Delay(500).ContinueWith(_ => _globalToggleBusy = false);
            }
        }
    }

    private bool _globalToggleBusy = false;
    private readonly Dictionary<uint, StorageSnap> _storageCache = new();

    private void CacheStorage(uint id, StorageSnap? snap)
    {
        if (snap == null) return;
        _storageCache[id] = snap;
    }

    private bool TryGetCachedStorage(uint id, out StorageSnap? snap)
        => _storageCache.TryGetValue(id, out snap);
    private async Task<EntityProbeResult> ProbeWithRetryAsync(uint entityId, bool log = true, int maxAttempts = 3)
    {
        int attempts = 0;
        int max = Math.Max(1, maxAttempts);
        while (attempts < max)
        {
            attempts++;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                var r = await _rust.ProbeEntityAsync(entityId, cts.Token);
                if (r.Exists) return r;

                // If the probe returned successfully but the entity doesn't exist, stop retrying.
                if (!r.Exists) break;

                if (attempts < max) await Task.Delay(500);
            }
            catch (Exception ex)
            {
                if (attempts >= max) throw;
                if (log) AppendLog($"[probe] #{entityId} attempt {attempts} failed: {ex.Message} – retrying …");
                await Task.Delay(1000);
                await EnsureConnectedAsync();
            }
        }
        return new EntityProbeResult(false, null, null);
    }

    private async Task RefreshDeviceStateAsync(SmartDevice dev, bool log = true, bool forcePull = false, int maxRetries = 3)
    {
        if (_rust is not RustPlusClientReal real) return;

        // Skip refreshing if it's a Group, because Groups don't exist on the Rust server
        if (dev.IsGroup) return;

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
                    if (log) AppendLog($"[stor/sub] #{dev.EntityId} failed: {subEx.Message}");
                }

                // (3) bei forcePull denselben Weg wie Refresh nutzen
                // OPTIMIERUNG: Wenn wir bereits Daten haben, verzichten wir beim normalen Refresh auf den Probe.
                // Storage Monitore sind eventbasiert; ein Probe triggert oft unnötige Ratelimits.
                bool shouldProbe = forcePull || dev.Storage == null || dev.Storage.ItemsCount == 0;
                
                if (shouldProbe)
                {
                    try
                    {
                        var rStor = await ProbeWithRetryAsync(dev.EntityId, log);
                        if (rStor.Exists)
                        {
                            dev.IsMissing = false;
                        }
                        else
                        {
                            // Wenn der Probe fehlschlägt, wir aber bereits valide Daten haben, 
                            // gehen wir von einem ratelimit aus und grauen NICHT aus.
                            if (dev.Storage != null && dev.Storage.ItemsCount > 0)
                            {
                                if (log) AppendLog($"#{dev.EntityId}: probe failed, but keeping existing data (ratelimit?).");
                            }
                            else
                            {
                                dev.IsMissing = true;
                                if (log) AppendLog($"#{dev.EntityId}: not reachable / demoted");
                            }
                        }
                    }
                    catch (Exception pullEx)
                    {
                        if (dev.Storage == null || dev.Storage.ItemsCount == 0)
                            dev.IsMissing = true;
                        if (log) AppendLog($"[stor/poll] #{dev.EntityId} probe EX: {pullEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                dev.Storage ??= new StorageSnapshot();
                dev.IsMissing = true;
                if (log) AppendLog($"[stor/refresh] #{dev.EntityId} EX: {ex.Message}");
            }
            return; // WICHTIG: Nach Storage-Spezialbehandlung nicht in den generischen Block laufen
        }

        // ===== Generisch (SmartAlarm & Co.) =====
        try
        {
            var r = await ProbeWithRetryAsync(dev.EntityId, log, maxRetries);

            // Kind nur setzen, wenn noch keines bekannt ist – nie ein bestehendes Kind überschreiben.
            // Verhindert, dass ein SmartSwitch durch eine verzögerte/falsche Probe-Antwort
            // zu einem SmartAlarm oder anderem Typ degradiert wird.
            if (!string.IsNullOrWhiteSpace(r.Kind) && string.IsNullOrWhiteSpace(dev.Kind))
            {
                dev.Kind = r.Kind;
            }

            dev.IsMissing = !r.Exists;

            _suppressToggleHandler = true;
            if ((dev.Kind ?? r.Kind)?.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Alarmstatus nie aus Probe übernehmen – immer false lassen
                dev.IsOn = false;
            }
            else
            {
                // IsOn nur aktualisieren wenn der Probe-Wert sich vom bekannten Zustand
                // unterscheidet. Verhindert, dass ein gerade manuell gesetzter Zustand
                // durch einen zeitversetzten Probe überschrieben wird.
                if (r.IsOn.HasValue && r.IsOn != dev.IsOn)
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
            dev.IsMissing = true;
            _vm?.NotifyDevicesChanged();
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
                _suppressToggleHandler = true;
                try
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
                            if (!string.IsNullOrWhiteSpace(s.Name)) ex.Name = s.Name;
                            if (!string.IsNullOrWhiteSpace(s.Kind)) ex.Kind = s.Kind;
                            ex.IsOn = s.IsOn;
                            // ⚠️ SmartAlarm nie als "true" aus Storage hochholen
                            if (string.Equals(ex.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase))
                                ex.IsOn = false;
                            ex.IsMissing = s.IsMissing;
                            if (!string.IsNullOrWhiteSpace(s.Alias)) ex.Alias = s.Alias;
                            ex.CustomIconId = s.CustomIconId;
                            ex.CustomIconShortName = s.CustomIconShortName;
                        }
                    }
                }
                finally
                {
                    _suppressToggleHandler = false;
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
        dev.IsEditing = true;
    }

    private void DeviceName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is TextBlock tb && tb.DataContext is SmartDevice dev)
        {
            e.Handled = true;
            dev.IsEditing = true;
        }
    }

    private void TxtRename_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is SmartDevice dev)
        {
            EndRenameEditing(dev, tb, save: true);
        }
    }

    private void TxtRename_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is SmartDevice dev)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                EndRenameEditing(dev, tb, save: true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EndRenameEditing(dev, tb, save: false);
            }
        }
    }

    private void TxtRename_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
        {
            tb.Focus();
            tb.SelectAll();
        }
    }

    private void EndRenameEditing(SmartDevice dev, TextBox tb, bool save)
    {
        if (!dev.IsEditing) return;

        if (save)
        {
            var be = tb.GetBindingExpression(TextBox.TextProperty);
            be?.UpdateSource();

            if (string.IsNullOrWhiteSpace(dev.Alias))
            {
                dev.Alias = null;
            }

            _vm?.Save();
            SaveOwnOverlayToJson();
            if (TrackingService.CloudSyncEnabled && RustPlusDesk.Services.Auth.SupabaseAuthManager.Client != null)
            {
                try
                {
                    _ = UploadDevicesSnapshotForCurrentServerAsync();
                }
                catch (Exception ex)
                {
                    AppendLog("[dev/sync] Error: " + ex.Message);
                }
            }
        }
        else
        {
            tb.Text = dev.Alias;
        }

        dev.IsEditing = false;
    }

public List<ExportedDeviceDto> Devices { get; set; } = new();

    private async void BtnDevicesExport_Click(object sender, RoutedEventArgs e)
    {
        if (IsFreeDeviceSyncLimitExceeded())
        {
            ShowPremiumLimitDialog(Properties.Resources.PremiumInfoDevicesDesc);
            return;
        }

        if (TrackingService.CloudSyncEnabled)
        {
            // Disable sync
            TrackingService.CloudSyncEnabled = false;
            TrackingService.UploadConsentGiven = false;
            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
        }
        else
        {
            // Enable sync
            if (!TrackingService.UploadConsentGiven)
            {
                var dlg = new CloudDisclaimerWindow { Owner = this };
                dlg.ShowDialog();
                if (!dlg.CloudSyncAccepted)
                {
                    TrackingService.CloudSyncEnabled = false;
                    TrackingService.UploadConsentGiven = false;
                    _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                    UpdateCloudSyncUI();
                    return;
                }
                TrackingService.CloudSyncEnabled = true;
                TrackingService.UploadConsentGiven = true;
                _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
            }
            else
            {
                TrackingService.CloudSyncEnabled = true;
                _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
            }

            // Immediately sync current state
            if (_vm.Selected != null && await EnsureConnectedAsync())
            {
                try
                {
                    await UploadDevicesSnapshotForCurrentServerAsync();
                }
                catch (Exception ex)
                {
                    AppendLog("[dev/sync] Error: " + ex.Message);
                }
            }
        }
        UpdateCloudSyncUI();
    }

    private async Task<int> UploadDevicesSnapshotForCurrentServerAsync(bool explicitWipe = false)
    {
        if (!explicitWipe && !_ownCloudRestoreReady)
        {
            AppendLog("[dev/cloud] Skipping device autosync until cloud restore is complete.");
            return 0;
        }

        var profile = _vm.Selected;
        if (profile?.Devices == null)
            throw new InvalidOperationException("No devices in current profile.");

        var canvasOverlay = BuildCurrentOverlaySaveDataForMe();
        var synced = await DeviceDataModule.UploadDevicesSnapshotAsync(GetServerKey(), _mySteamId, profile.Devices, canvasOverlay, explicitWipe);
        if (synced > 0)
            MarkDevicesCloudSynced(synced);

        return synced;
    }

    private ExportedDeviceDto MapDeviceToDto(SmartDevice d)
    {
        return DeviceDataModule.MapDeviceToDto(d);
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
            // 1) Alle Team-Mitglieder + eigene SteamId inkludieren
            var allSteamIds = TeamMembers
                .Where(tm => tm.SteamId != 0)
                .Select(tm => tm.SteamId)
                .ToHashSet();

            // Sicherstellen dass eigene SteamId immer dabei ist (für Neuinstallation / Cloud-Import eigener Devices)
            if (_mySteamId != 0)
                allSteamIds.Add(_mySteamId);

            // 2) Cloud-Fetch (anon key genügt, kein Discord-Login nötig)
            //    Parallel für alle IDs – Fehler einzelner IDs werden ignoriert
            if (Services.Auth.SupabaseAuthManager.Client != null && TrackingService.CloudSyncEnabled)
            {
                var fetchTasks = allSteamIds.Select(sid => TryFetchAndUpdateOverlayAsync(sid));
                await Task.WhenAll(fetchTasks);
            }

            // 3) Import-Kandidaten sammeln (Cloud zuerst, Fallback auf lokale Datei)
            var items = new List<DeviceImportItem>();

            foreach (var sid in allSteamIds)
            {
                // TeamMember-VM für diesen SteamId (oder Dummy für eigene ID ohne VM-Eintrag)
                var tm = TeamMembers.FirstOrDefault(t => t.SteamId == sid)
                      ?? new TeamMemberVM { SteamId = sid, Name = sid == _mySteamId ? "Me" : sid.ToString() };

                OverlaySaveData? data = null;

                // Zuerst Cloud versuchen
                if (Services.Auth.SupabaseAuthManager.Client != null && TrackingService.CloudSyncEnabled)
                {
                    try { data = await OverlayDataModule.FetchOverlayFromServerAsync(GetServerKey(), sid); }
                    catch { /* Cloud nicht erreichbar – lokale Datei als Fallback */ }
                }

                // Fallback: lokale Datei
                if (data == null)
                {
                    var path = GetOverlayJsonPathForPlayerServer(sid);
                    if (File.Exists(path))
                    {
                        try
                        {
                            var json = File.ReadAllText(path);
                            data = System.Text.Json.JsonSerializer.Deserialize<OverlaySaveData>(json);
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[dev/import] Can't parse local overlay for {sid}: {ex.Message}");
                        }
                    }
                }

                if (data?.Devices == null || data.Devices.Count == 0)
                    continue;

                foreach (var d in data.Devices)
                {
                    CollectIndividualDevices(items, d, tm);
                }
            }

            if (items.Count == 0)
            {
                MessageBox.Show("No device exports found for your team / server.\n\n" +
                                "Make sure Cloud Sync is enabled and at least one team member has synced their devices.",
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
                    // Rekursiver Import via DTO - prüfen auf allen Ebenen
                    if (FindDeviceById(_vm.Selected.Devices, it.OriginalDto.EntityId) != null)
                        continue;

                    var dev = MapDtoToDeviceFiltered(it.OriginalDto);
                    _vm.Selected.Devices.Add(dev);
                }
                else
                {
                    // Fallback
                    if (FindDeviceById(_vm.Selected.Devices, it.EntityId) != null)
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
            UpdateCloudSyncUI();

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

    private int _refreshAllBusy = 0;
    private async Task RefreshAllDevicesStatusAsync(int maxRetries = 3)
    {
        if (Interlocked.Exchange(ref _refreshAllBusy, 1) == 1) return;
        try
        {
        if (_logicEngineRunningAction)
        {
            AppendLog("[LogicEngine] Manual refresh blocked: Logic Engine is executing an action.");
            return;
        }

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

        AppendLog("Updating Device Status (sequential).");
        
        foreach (var d in list)
        {
            try
            {
                await RefreshDeviceRecursiveAsync(d, maxRetries);
                await Task.Delay(250); // Increased gap to prevent API spam
            }
            catch (Exception ex)
            {
                AppendLog($"Error refreshing {d.DisplayName}: {ex.Message}");
            }
        }
        
        _vm.Save();
        AppendLog("Refresh completed.");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshAllBusy, 0);
        }
    }

    private async Task RefreshDeviceRecursiveAsync(SmartDevice d, int maxRetries = 3)
    {
        try
        {
            if (!d.IsGroup)
            {
                if ((DateTime.UtcNow - d.LastPolledAt).TotalSeconds < 15)
                {
                    // Skip if recently polled
                    return;
                }
                
                await RefreshDeviceStateAsync(d, log: true, forcePull: true, maxRetries: maxRetries);
                d.LastPolledAt = DateTime.UtcNow;
            }
            else
            {
                // Gruppen haben keinen direkten Status, aber wir refreshen Kinder
                d.IsMissing = false; // groups are never missing
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
        bool already = FindDeviceById(_vm.Selected.Devices, d.EntityId) != null;

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

    private void CollectIndividualDevices(List<DeviceImportItem> items, ExportedDeviceDto d, TeamMemberVM tm)
    {
        if (d.IsGroup)
        {
            if (d.Children != null)
            {
                foreach (var child in d.Children)
                {
                    CollectIndividualDevices(items, child, tm);
                }
            }
        }
        else
        {
            AddDeviceToImportItems(items, d, tm);
        }
    }

    private SmartDevice MapDtoToDeviceFiltered(ExportedDeviceDto dto)
    {
        var dev = DeviceDataModule.MapDtoToDevice(dto);
        FilterExistingDevicesRecursively(dev);
        return dev;
    }

    private void FilterExistingDevicesRecursively(SmartDevice parent)
    {
        if (parent.Children == null || parent.Children.Count == 0) return;
        
        for (int i = parent.Children.Count - 1; i >= 0; i--)
        {
            var child = parent.Children[i];
            if (FindDeviceById(_vm?.Selected?.Devices, child.EntityId) != null)
            {
                parent.Children.RemoveAt(i);
            }
            else if (child.IsGroup)
            {
                FilterExistingDevicesRecursively(child);
            }
        }
    }

    private SmartDevice MapDtoToDevice(ExportedDeviceDto dto)
    {
        return DeviceDataModule.MapDtoToDevice(dto);
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

        // Collect devices that actually need probing (no kind yet and NOT in storage cache)
        var toProbe = _vm.Selected.Devices.Where(d => 
            string.IsNullOrEmpty(d.Kind) && 
            !d.IsGroup && 
            (_rust is not RustPlusClientReal rpc || !rpc.TryGetCachedStorage(d.EntityId, out _))
        ).ToList();

        // Check if some devices can be identified from cache without probing
        if (_rust is RustPlusClientReal real)
        {
            foreach (var d in _vm.Selected.Devices.Where(d => string.IsNullOrEmpty(d.Kind) && !d.IsGroup).ToList())
            {
                if (real.TryGetCachedStorage(d.EntityId, out _))
                {
                    d.Kind = "StorageMonitor"; // If it's in the storage cache, it's definitely a storage monitor
                }
            }
        }

        // Re-calculate toProbe after identifying devices from cache
        toProbe = _vm.Selected.Devices.Where(d => 
            string.IsNullOrEmpty(d.Kind) && 
            !d.IsGroup && 
            (_rust is not RustPlusClientReal rpc || !rpc.TryGetCachedStorage(d.EntityId, out _))
        ).ToList();

        if (toProbe.Count == 0) return;

        AppendLog($"[prime] probing {toProbe.Count} device kinds (parallel) …");
        
        // Limit concurrency to 3 simultaneous probes to avoid overwhelming the socket
        using var semaphore = new SemaphoreSlim(3);
        var tasks = toProbe.Select(async d =>
        {
            await semaphore.WaitAsync();
            try
            {
                var r = await ProbeWithRetryAsync(d.EntityId, false);
                if (!string.IsNullOrWhiteSpace(r.Kind))
                    d.Kind = r.Kind;
                d.IsMissing = !r.Exists;
                
                // Update state immediately from probe — only when it actually differs
                // to avoid overwriting a state the user just set manually.
                // Use Dispatcher.Invoke so that the UI-thread-only _suppressToggleHandler
                // flag is set and cleared on the correct thread (parallel tasks run off-thread).
                if (r.Exists && r.IsOn.HasValue && r.IsOn != d.IsOn)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _suppressToggleHandler = true;
                        d.IsOn = r.IsOn;
                        _suppressToggleHandler = false;
                    });
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[prime] #{d.EntityId} err: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
                await Task.Delay(150);
            }
        });


        await Task.WhenAll(tasks);
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

    private void DeviceIcon_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (sender is FrameworkElement fe && fe.DataContext is SmartDevice dev)
            {
                var dlg = new RustPlusDesk.Views.Windows.ChangeDeviceIconDialog(dev.CustomIconId, dev.CustomIconShortName)
                {
                    Owner = this
                };
                
                _activeDialog = dlg;
                ApplyWindowBlur();
                if (Root != null)
                {
                    Root.IsHitTestVisible = false;
                }

                dlg.Closed += (s, ev) =>
                {
                    if (ReferenceEquals(_activeDialog, dlg))
                    {
                        _activeDialog = null;
                    }
                    RemoveWindowBlur();
                    if (Root != null)
                    {
                        Root.IsHitTestVisible = true;
                    }

                    if (dlg.IsSaved)
                    {
                        if (dlg.IsResetClicked)
                        {
                            dev.CustomIconId = null;
                            dev.CustomIconShortName = null;
                        }
                        else
                        {
                            dev.CustomIconId = dlg.SelectedIconId;
                            dev.CustomIconShortName = dlg.SelectedIconShortName;
                        }
                        
                        if (_vm != null)
                        {
                            _vm.Save();
                        }

                        // Save local and push sync
                        SaveOwnOverlayToJson();
                        if (TrackingService.CloudSyncEnabled && RustPlusDesk.Services.Auth.SupabaseAuthManager.Client != null)
                        {
                            try
                            {
                                _ = UploadDevicesSnapshotForCurrentServerAsync();
                            }
                            catch (Exception ex)
                            {
                                AppendLog("[dev/sync] Error: " + ex.Message);
                            }
                        }
                    }
                };

                dlg.Show();
                CenterActiveDialog();
            }
        }
        catch (Exception ex)
        {
            AppendLog("[dev/icon] Dialog error: " + ex.Message);
        }
    }

    private void ApplyWindowBlur()
    {
        var blur = new System.Windows.Media.Effects.BlurEffect { Radius = 0 };
        if (Root != null)
        {
            Root.Effect = blur;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 10, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, anim);
        }
    }

    private void RemoveWindowBlur()
    {
        if (Root != null && Root.Effect is System.Windows.Media.Effects.BlurEffect blur)
        {
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            anim.Completed += (s, e) =>
            {
                if (ReferenceEquals(Root.Effect, blur))
                {
                    Root.Effect = null;
                }
            };
            blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, anim);
        }
    }

    internal static List<ShopSearchControl.AutocompleteItem> SearchItems(string query)
    {
        EnsureNewItemDbLoaded();
        string cleanQuery = (query ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            return new List<ShopSearchControl.AutocompleteItem>();
        }

        return sItemsById.Values
            .Where(ii => !string.IsNullOrWhiteSpace(ii.Display) && 
                         (ii.Display.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase) || 
                          (ii.ShortName != null && ii.ShortName.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))))
            .Take(50)
            .Select(ii => new ShopSearchControl.AutocompleteItem
            {
                Id = ii.Id,
                Display = ii.Display,
                ShortName = ii.ShortName ?? "",
                Icon = ResolveItemIcon(ii.Id, ii.ShortName, 32)
            })
            .ToList();
    }
}
