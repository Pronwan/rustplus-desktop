using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private async Task EnsureWebView2Async()
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustPlusDesk", "WebView2");
        Directory.CreateDirectory(dataFolder);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        _webView = new WebView2();
        WebViewHost.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B3A4A"));
        WebViewHost.Children.Add(_webView);
        Panel.SetZIndex(_webView, 0);           // WebView standardmaessig unten

        await _webView.EnsureCoreWebView2Async(env);
        _webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _webView.Visibility = Visibility.Collapsed;

        // Optional: etwas "normaleren" UA setzen
        _webView.CoreWebView2.Settings.UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    }



    private async Task UpdateServerStatusAsync()
    {
        try
        {
            if (_rust is RustPlusClientReal real && _vm.Selected?.IsConnected == true)
            {
                var st = await real.GetServerStatusAsync();
                if (st != null && st.Players >= 0)
                {
                    _vm.ServerPlayers = $"{st.Players}/{st.MaxPlayers}";
                    _vm.ServerQueue = (st.Queue >= 0) ? st.Queue.ToString() : "0";
                    
                    if (!string.IsNullOrWhiteSpace(st.TimeString))
                        _vm.ServerTime = st.TimeString;
                }
            }
        }
        catch
        {
            // leise weiter - der Poll laeuft einfach erneut
        }
    }



    private void BtnAddServer_Click(object sender, RoutedEventArgs e)
    {
        var host = Microsoft.VisualBasic.Interaction.InputBox("Server IP/Host:", "Server hinzufügen", "127.0.0.1");
        var portStr = Microsoft.VisualBasic.Interaction.InputBox("Companion-Port:", "Server hinzufügen", "28082");
        var token = Microsoft.VisualBasic.Interaction.InputBox("Player-Token (Rust+):", "Server hinzufügen", "");
        var proxy = Microsoft.VisualBasic.Interaction.InputBox("Facepunch-Proxy verwenden? (y/n)", "Server hinzufügen", "n");

        if (int.TryParse(portStr, out var port) && !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(token))
        {
            var prof = new ServerProfile
            {
                Name = $"{host}:{port}",
                Host = host,
                Port = port,
                SteamId64 = _vm.SteamId64,
                PlayerToken = token,
                UseFacepunchProxy = proxy.Trim().ToLowerInvariant().StartsWith("y")
            };
            _vm.AddServer(prof);
            _vm.Save();
        }
        else
        {
            MessageBox.Show("Ungültige Eingaben.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ListServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _lastChatTsForCurrentServer = null;

        if (_vm.Servers.Any(s => s.IsConnected || s.IsFullConnected))
        {
            AppendLog("Server selection changed. Disconnecting from previous server...");
            await HardResetAsync(reconnect: false);
        }

        if (_vm.Selected is { } prof)
        {
            if (!string.IsNullOrWhiteSpace(prof.SteamId64))
                _vm.SteamId64 = prof.SteamId64;

            // Trigger soft connect to devices only if there are any devices to control
            if (!prof.IsConnected && prof.Devices.Any())
            {
                await PerformConnectDevicesOnlyAsync(prof);
            }
        }

        HydrateSteamUiFromStorage();
        RegisterAllHotkeys();
        ActivateHotkeysForCurrentServer();
        UpdateRustMapsUi();
    }

    private async Task PerformConnectDevicesOnlyAsync(ServerProfile profile)
    {
        if (profile == null) return;
        try
        {
            AppendLog($"Soft-connecting to {profile.Name} (Devices Only)...");
            await _rust.ConnectAsync(profile);
            profile.IsConnected = true;
            profile.IsFullConnected = false;

            if (_rust is RustPlusClientReal real)
            {
                real.EnsureEventsHooked();
                
                // Hook local handlers
                real.ConnectionLost -= OnConnectionLost;
                real.ConnectionLost += OnConnectionLost;

                var allIds = profile.Devices.Select(d => d.EntityId).Distinct().ToList();
                if (allIds.Any())
                {
                    await real.PrimeSubscriptionsAsync(allIds);
                }
                
                // Refresh device status in background
                _ = RefreshAllDevicesStatusAsync(maxRetries: 1);
            }
            
            // Start the server status loop (players/time) even for soft connect
            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            _ = PollServerStatusLoopAsync(_statusCts.Token);

            // Start A2S player polling so the Players tab works on soft connect too
            TrackingService.StartPolling(profile.Host ?? "", profile.Port, profile.Name ?? "", profile.BattleMetricsId);
            _ = TrackingService.FetchOnlinePlayersNowAsync();

            _ = SearchRustMapsAsync(false);

            AppendLog("Soft-connect complete.");

        }
        catch (Exception ex)
        {
            AppendLog($"Soft-connect failed: {ex.Message}");
        }
    }

    private async Task PollServerStatusLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_rust is RustPlusClientReal real)
                {
                    var st = await real.GetServerStatusAsync(ct);
                    if (st != null && st.Players >= 0)
                    {
                        _vm.ServerPlayers = $"{st.Players}/{st.MaxPlayers}";
                        _vm.ServerQueue = (st.Queue >= 0) ? st.Queue.ToString() : "0";
                        
                        if (!string.IsNullOrWhiteSpace(st.TimeString))
                            _vm.ServerTime = st.TimeString;
                    }
                }
            }
            catch { /* Keep last known values on error */ }

            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { }
        }
    }

    private async Task<bool> PerformConnectAsync(bool silent)
    {
        if (!silent)
        {
            await HardResetAsync(reconnect: false);
            _shopTimer?.Stop();
            _shopTimer = null;
            StopDynPolling();
            StopTeamPolling();
            TeamMembers.Clear();

            _avatarCache.Clear();
            _lastPresence.Clear();
            ClearAllDeathPins();
            ClearAllToggleBusy();
            _lastShops.Clear();
            _shopLifetimes.Clear();
            _knownShopIds.Clear();
            _initialShopSnapshotTimeUtc = DateTime.MinValue;
            _alertsNeedRebaseline = true;
            _lastChatSendUtc = DateTime.MinValue;

            foreach (var el in _shopEls.Values)
                Overlay.Children.Remove(el);
            _shopEls.Clear();
        }
        else
        {
            // Ensure the interface disconnects before recreating
            try { await _rust.DisconnectAsync(); } catch { }

            _shopTimer?.Stop();
            StopDynPolling(clearKnown: false);
            StopTeamPolling();
            _alertsNeedRebaseline = true;
        }

        if (_vm.Selected is null)
        {
            if (!silent) ShowInfoSnackbar(Properties.Resources.SnackbarTitleConnection, Properties.Resources.PleaseSelectServerFirst, WpfUi.ControlAppearance.Info);
            return false;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.BusyText = "Connecting …";

            AppendLog($"Connecting to ws://{_vm.Selected.Host}:{_vm.Selected.Port} …");
            await _rust.ConnectAsync(_vm.Selected);
            AppendLog("Connected.");
            _connectedProfile = _vm.Selected;

            TrackingService.StartPolling(_vm.Selected.Host ?? "", _vm.Selected.Port, _vm.Selected.Name ?? "", _vm.Selected.BattleMetricsId);

            // Shorter initial delay — just enough for the WS to be ready
            await Task.Delay(500);

            var real = _rust as RustPlusClientReal;
            if (real != null)
            {
                real.StorageSnapshotReceived -= OnStorageSnapshot;
                real.StorageSnapshotReceived += OnStorageSnapshot;
                real.ConnectionLost -= OnConnectionLost;
                real.ConnectionLost += OnConnectionLost;
                
                // Add global chat subscription for background bot commands
                real.TeamChatReceived -= Real_TeamChatReceived;
                real.TeamChatReceived += Real_TeamChatReceived;

                real.EnsureEventsHooked();
            }

            _lastChatTsForCurrentServer = null;

            if (real != null)
            {
                try { await real.PrimeTeamChatAsync(); }
                catch (Exception ex) { AppendLog("[chat] prime error: " + ex.Message); }
            }

            _vm.IsBusy = false;
            _vm.BusyText = "";
            _vm.IsInitializing = true;

            // Start atomic parallel loading block to prevent UI "stuttering" during sequential awaits
            var initTasks = new List<Task>();
            
            initTasks.Add(LoadMapAsync());
            initTasks.Add(UpdateServerStatusAsync());
            initTasks.Add(LoadTeamAsync());
            
            // Rehydrate local cache immediately (sync)
            RehydrateDevicesFromStorageInto(_vm.Selected);

            RehydrateCamerasFromStorageInto(_vm.Selected);
            SwitchCameraSourceTo(_vm.Selected);
            
            // Start probing device kinds in parallel (optimized in MainWindow.Devices.cs)
            initTasks.Add(PrimeDeviceKindsAsync());



            ClearUserOverlayElements();
            _visibleOverlayOwners.Add(_mySteamId);
            LoadOverlayFromDiskForPlayer(_mySteamId);

            // Wait for core initialization to complete
            await Task.WhenAll(initTasks);
            
            // Core data is now loaded (_worldSizeS is available)
            if (TrackingService.AutoLoadShops)
            {
                Dispatcher.Invoke(() => ChkShops.IsChecked = true);
            }
            
            _vm.IsInitializing = false;
            _vm.Selected.IsConnected = true;
            _vm.Selected.IsFullConnected = true;

            _vm.NotifyDevicesChanged();
            _ = SearchRustMapsAsync(false);
            AppendLog($"Connection initialization complete. Server: {_vm.Selected.Name}");

            // Finally, refresh all device statuses to ensure the UI reflects the current server state
            _ = RefreshAllDevicesStatusAsync(maxRetries: 1);

            // Finally, prime subscriptions for all devices to receive real-time updates
            if (real != null && _vm.Selected?.Devices?.Any() == true)
            {
                try
                {
                    // Batching all entity subscriptions into one call
                    var allIds = _vm.Selected.Devices.Select(d => d.EntityId).Distinct().ToList();
                    await real.PrimeSubscriptionsAsync(allIds);
                    AppendLog($"Subscribed to {allIds.Count} entities.");
                }
                catch (Exception ex)
                {
                    AppendLog("PrimeSubscriptions Error: " + ex.Message);
                }
            }

            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            _ = PollServerStatusLoopAsync(_statusCts.Token);
            _statusTimer.Start();

            StartTeamPolling();
            if (_overlayToolsVisible)
            {
                RebuildOverlayTeamBar();
            }

            // Removed redundant 2s delayed RefreshAllDevicesStatusAsync to avoid 'demoted' logs.
            // Core device status is already fetched during PrimeDeviceKindsAsync.
        }
        catch (Exception ex)
        {
            _vm.IsInitializing = false;
            _vm.IsBusy = false;
            _vm.BusyText = "";
            AppendLog("Fehler: " + ex.Message);
            if (!silent) MessageBox.Show($"Connection failed: {ex.Message}");
            return false;
        }

        return true;
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_vm.Selected is null) { AppendLog("No server selected."); return false; }
        if (_vm.Selected.IsConnected) return true;

        AppendLog($"Verbinde zu ws://{_vm.Selected.Host}:{_vm.Selected.Port} …");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            await _rust.ConnectAsync(_vm.Selected, cts.Token);
            _vm.Selected.IsConnected = true;
            AppendLog("Connected.");
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("Connect failed: " + ex.Message);
            return false;
        }
    }
}
