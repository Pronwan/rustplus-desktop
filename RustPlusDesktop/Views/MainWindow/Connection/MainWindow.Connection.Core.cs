using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

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

        // Optional: etwas "normaleren" UA setzen
        _webView.CoreWebView2.Settings.UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
        _webView.NavigationCompleted += WebView_NavigationCompleted;
    }

    private async void BtnSteamLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var loopback = new SteamOpenIdLoopbackService();
            var sid = await loopback.SignInAsync();
            _vm.SteamId64 = sid;
            TrackingService.SteamId64 = sid;
            TxtSteamId.Text = sid;
            AppendLog($"Steam angemeldet (Loopback): {sid}");
            _vm.Save();
            HydrateSteamUiFromStorage();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Steam-Login fehlgeschlagen: " + ex.Message);
        }
    }

    private async Task UpdateServerStatusAsync()
    {
        try
        {
            if (_rust is RustPlusClientReal real && _vm.Selected?.IsConnected == true)
            {
                var st = await real.GetServerStatusAsync();
                if (st != null)
                {
                    _vm.ServerPlayers = (st.Players >= 0 && st.MaxPlayers >= 0)
                        ? $"{st.Players}/{st.MaxPlayers}" : "–";

                    _vm.ServerQueue = (st.Queue >= 0)
                        ? st.Queue.ToString()
                        : "–";

                    _vm.ServerTime = string.IsNullOrWhiteSpace(st.TimeString)
                        ? "–"
                        : st.TimeString;
                }
            }
        }
        catch
        {
            // leise weiter - der Poll laeuft einfach erneut
        }
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_webView?.Source is null) return;
        var url = _webView.Source.ToString();
        if (_steam.TryExtractSteamId64FromReturnUrl(url, out var sid))
        {
            _vm.SteamId64 = sid;
            TxtSteamId.Text = sid;
            AppendLog($"Steam angemeldet: {sid}");
            _vm.Save();
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

    private void ListServers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _lastChatTsForCurrentServer = null;

        if (_vm.Selected is { } prof && !string.IsNullOrWhiteSpace(prof.SteamId64))
            _vm.SteamId64 = prof.SteamId64;

        HydrateSteamUiFromStorage();
        RegisterAllHotkeys();
        ActivateHotkeysForCurrentServer();
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
                    if (st != null)
                    {
                        _vm.ServerPlayers = (st.Players >= 0 && st.MaxPlayers >= 0)
                            ? $"{st.Players}/{st.MaxPlayers}" : "–";

                        _vm.ServerQueue = (st.Queue >= 0) ? st.Queue.ToString() : "–";
                        _vm.ServerTime = string.IsNullOrWhiteSpace(st.TimeString) ? "–" : st.TimeString;
                    }
                }
            }
            catch { }

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
            _shopTimer?.Stop();
            StopDynPolling(clearKnown: false);
            StopTeamPolling();
            _alertsNeedRebaseline = true;
        }

        if (_vm.Selected is null)
        {
            if (!silent) MessageBox.Show("Please chose a server.");
            return false;
        }

        try
        {
            _vm.IsBusy = true;
            _vm.BusyText = "Connecting …";

            AppendLog($"Connecting to ws://{_vm.Selected.Host}:{_vm.Selected.Port} …");
            await _rust.ConnectAsync(_vm.Selected);
            _vm.Selected.IsConnected = true;
            AppendLog("Connected.");
            _connectedProfile = _vm.Selected;

            TrackingService.StartPolling(_vm.Selected.Host ?? "", _vm.Selected.Port, _vm.Selected.Name ?? "");

            await Task.Delay(1000);
            var real = _rust as RustPlusClientReal;
            if (real != null)
            {
                real.StorageSnapshotReceived -= OnStorageSnapshot;
                real.StorageSnapshotReceived += OnStorageSnapshot;
                real.ConnectionLost -= OnConnectionLost;
                real.ConnectionLost += OnConnectionLost;
                real.EnsureEventsHooked();
            }

            _lastChatTsForCurrentServer = null;

            if (real != null)
            {
                try
                {
                    await real.PrimeTeamChatAsync();
                }
                catch (Exception ex)
                {
                    AppendLog("[chat] prime error: " + ex.Message);
                }
            }
            _vm.IsBusy = false;
            _vm.BusyText = "";
            await LoadMapAsync();
            await StartPairingListenerUiAsync();

            if (TrackingService.AutoLoadShops)
            {
                Dispatcher.Invoke(() => ChkShops.IsChecked = true);
            }

            ClearUserOverlayElements();
            _visibleOverlayOwners.Add(_mySteamId);
            LoadOverlayFromDiskForPlayer(_mySteamId);

            RehydrateDevicesFromStorageInto(_vm.Selected);
            if (real != null && _vm.Selected?.Devices?.Any() == true)
            {
                var storageIds = _vm.Selected.Devices
                    .Where(d => string.Equals(d.Kind, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.EntityId)
                    .ToList();

                if (storageIds.Count > 0)
                {
                    try
                    {
                        await real.PrimeSubscriptionsAsync(storageIds);
                        AppendLog($"PrimeSubscriptions: {storageIds.Count} StorageMonitors.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog("PrimeSubscriptions Error: " + ex.Message);
                    }
                }
            }

            await PrimeDeviceKindsAsync();
            _vm.NotifyDevicesChanged();
            AppendLog($"Devices rehydrated: {_vm.Selected.Devices?.Count ?? 0}");

            RehydrateCamerasFromStorageInto(_vm.Selected);
            SwitchCameraSourceTo(_vm.Selected);
            AppendLog($"Cams rehydrated: {_vm.Selected.CameraIds?.Count ?? 0}");

            if (real != null && _vm.Selected?.Devices?.Any() == true)
            {
                try
                {
                    await real.PrimeSubscriptionsAsync(_vm.Selected.Devices.Select(d => d.EntityId));
                    AppendLog($"PrimeSubscriptions: {_vm.Selected.Devices.Count} IDs.");
                }
                catch (Exception ex)
                {
                    AppendLog("PrimeSubscriptions Error: " + ex.Message);
                }
            }

            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            _ = PollServerStatusLoopAsync(_statusCts.Token);
            await UpdateServerStatusAsync();
            _statusTimer.Start();

            StartTeamPolling();
            await LoadTeamAsync();
            if (_overlayToolsVisible)
            {
                RebuildOverlayTeamBar();
            }
        }
        catch (Exception ex)
        {
            _vm.IsBusy = false;
            _vm.BusyText = "";
            AppendLog("Fehler: " + ex.Message);
            if (!silent) MessageBox.Show($"Connection failed: {ex.Message}");
            return false;
        }

        _storageTimer?.Stop();
        _storageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _storageTimer.Tick += async (_, __) =>
        {
            if (_storageTickBusy) return;
            _storageTickBusy = true;
            try
            {
                var sel = _connectedProfile ?? _vm?.Selected;
                var devs = sel?.Devices;

                if (devs == null || devs.Count == 0)
                    return;

                var snapshot = devs
                    .Where(sd => RustPlusClientReal.IsStorageDevice(sd))
                    .ToList();

                if (snapshot.Count == 0)
                    return;

                foreach (var sd in snapshot)
                {
                    try
                    {
                        await RefreshDeviceStateAsync(sd, log: true, forcePull: true);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[stor/poll] #{sd.EntityId} err: {ex.Message}");
                    }
                }
            }
            finally
            {
                _storageTickBusy = false;
            }
        };
        _storageTimer.Start();
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
