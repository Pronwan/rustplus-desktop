using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Wpf.Ui.Controls;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.Text.Json.Nodes;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Auth;
using RustPlusDesk.Views.Windows;
using System.Windows;
using System.Windows.Media;
using RustPlusDesk.Models;

namespace RustPlusDesk.Views
{
    public partial class MainWindow
    {
        private bool _isRustMapsSearching;
        private bool _isMap3DPreparing;
        private bool _isMap3DActive;
        private WebView2? _map3DWebView;
        private string? _currentMapFolderPath;
        private EventHandler<CoreWebView2WebResourceRequestedEventArgs>? _map3DResourceRequestHandler;
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Map3DResourceNameMap = new(() =>
            Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .Where(name => name.StartsWith("Map3DViewer/", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(NormalizeMap3DResourceName, name => name, StringComparer.OrdinalIgnoreCase));

        private ServerProfile? _copyMapSourceProfile;

        /// <summary>
        /// For offline/placeholder map profiles, the API is not connected so _worldSizeS is never
        /// set via GetMapAsync. This method restores it from a previously-parsed map_data.json so
        /// that the heatmap overlay rect and 3D texture UV are computed correctly.
        /// </summary>
        private void TryRestoreWorldSizeFromCachedMapData(ServerProfile prof, System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            try
            {
                string folder = Map3DLocalBuildService.GetPreparedFolderPath(prof, prof.RustMapsMapId);
                string mapDataPath = System.IO.Path.Combine(folder, "map_data.json");
                if (!System.IO.File.Exists(mapDataPath)) return;

                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(mapDataPath));
                if (!doc.RootElement.TryGetProperty("size", out var sizeEl)) return;
                int parsedSize = sizeEl.GetInt32();
                if (parsedSize <= 0) return;

                double wDip = bitmap.PixelWidth * (96.0 / bitmap.DpiX);
                double hDip = bitmap.PixelHeight * (96.0 / bitmap.DpiY);

                _worldSizeS = parsedSize;
                _worldRectPx = ComputeWorldRectFromWorldSize(wDip, hDip, _worldSizeS, 2000);

                AppendLog($"[Offline Map] Restored worldSize={parsedSize} from cached map_data.json. worldRectPx=[{(int)_worldRectPx.X},{(int)_worldRectPx.Y},{(int)_worldRectPx.Width}x{(int)_worldRectPx.Height}]");
            }
            catch (Exception ex)
            {
                AppendLog($"[Offline Map] Could not restore worldSize from map_data.json: {ex.Message}");
            }
        }

        public void UpdateRustMapsUi()
        {
            var profile = _vm.Selected;
            if (profile == null)
            {
                RustMapsOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            bool isPlaceholder = !string.IsNullOrEmpty(profile.LocalMapFilePath);
            if (!isPlaceholder && !profile.IsConnected && !profile.IsFullConnected)
            {
                RustMapsOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            RustMapsOverlay.Visibility = Visibility.Visible;

            if (isPlaceholder)
            {
                TxtRustMapsStatus.Text = "Offline Map";
                BtnOpenRustMaps.IsEnabled = false;
            }
            else if (_isRustMapsSearching)
            {
                TxtRustMapsStatus.Text = "Searching...";
                BtnOpenRustMaps.IsEnabled = false;
            }
            else if (!string.IsNullOrEmpty(profile.RustMapsMapId))
            {
                TxtRustMapsStatus.Text = "RustMaps";
                BtnOpenRustMaps.IsEnabled = true;
            }
            else
            {
                TxtRustMapsStatus.Text = "No Map Found";
                BtnOpenRustMaps.IsEnabled = false;
            }

            bool canUseLocal3DMap = (SupabaseAuthManager.IsDiscordAuthenticated || SupabaseAuthManager.IsEmailAuthenticated)
                && (profile.IsFullConnected || isPlaceholder);
            BtnOpen3DMap.Visibility = canUseLocal3DMap ? Visibility.Visible : Visibility.Collapsed;
            BtnOpen3DMap.IsEnabled = canUseLocal3DMap && !_isMap3DPreparing;
            TxtOpen3DMap.Text = _isMap3DPreparing ? "Preparing..." : _isMap3DActive ? "2D Map" : "3D Map";
            IconOpen3DMap.Symbol = _isMap3DActive ? Wpf.Ui.Controls.SymbolRegular.Map20 : Wpf.Ui.Controls.SymbolRegular.Cube20;

            string folderPath = Map3DLocalBuildService.GetPreparedFolderPath(profile, profile.RustMapsMapId);
            bool mapDataExists = System.IO.File.Exists(System.IO.Path.Combine(folderPath, "map_data.json"));
            BtnToggleHeatmap.Visibility = mapDataExists ? Visibility.Visible : Visibility.Collapsed;

            if (RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium)
            {
                BtnSendMapToDiscord.Visibility = Visibility.Visible;
            }
            else
            {
                BtnSendMapToDiscord.Visibility = Visibility.Collapsed;
            }
        }

        public async Task SearchRustMapsAsync(bool forceRefetch = false)
        {
            var profile = _vm.Selected;
            if (profile == null)
            {
                RustMapsOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (!profile.IsConnected && !profile.IsFullConnected)
            {
                RustMapsOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            // 1. If we already have a Map ID and are NOT forcing a refetch, show UI immediately. Local 3D wipe detection is handled by the saved map texture hash.
            if (!forceRefetch && !string.IsNullOrEmpty(profile.RustMapsMapId))
            {
                _isRustMapsSearching = false;
                UpdateRustMapsUi();

                return;
            }

            // 2. Perform a full fetch/refetch (show searching state)
            _isRustMapsSearching = true;
            UpdateRustMapsUi();

            try
            {
                var match = await FetchRustMapsServerMatchAsync(profile.Host, profile.Port);
                if (match != null)
                {
                    DateTime? lastWipe = null;
                    if (DateTime.TryParse(match.lastWipeUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime lw))
                    {
                        lastWipe = lw;
                    }

                    profile.RustMapsMapId = match.mapId;
                    profile.RustMapsWipeTime = lastWipe;
                    profile.RustMapsFetchTime = DateTime.UtcNow;
                    _vm.Save();

                    AppendLog($"[RustMaps] Resolved map {match.mapId} for {profile.Name}.");
                }
                else
                {
                    profile.RustMapsMapId = null;
                    profile.RustMapsWipeTime = null;
                    profile.RustMapsFetchTime = null;
                    _vm.Save();

                    AppendLog($"[RustMaps] Map not found on RustMaps for {profile.Name}.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[RustMaps] Error during map resolution: {ex.Message}");
            }
            finally
            {
                _isRustMapsSearching = false;
                UpdateRustMapsUi();
            }
        }

        private async Task<RustMapsMatch?> FetchRustMapsServerMatchAsync(string host, int companionPort)
        {
            if (string.IsNullOrEmpty(host)) return null;

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) RustPlusDesk");

            int gamePort = companionPort - 67;

            // 1. Try exact query (IP + standard Game Port offset)
            if (gamePort > 0)
            {
                var url = $"https://api.rustmaps.com/internal/v1/servers/search?input={Uri.EscapeDataString($"{host}:{gamePort}")}&onlyServersWithPlayers=true";
                var match = await QueryRustMapsApiAsync(client, url);
                if (match != null) return match;

                url = $"https://api.rustmaps.com/internal/v1/servers/search?input={Uri.EscapeDataString($"{host}:{gamePort}")}";
                match = await QueryRustMapsApiAsync(client, url);
                if (match != null) return match;
            }

            // 2. Fallback: Search with IP only and find the closest match
            var fallbackUrl = $"https://api.rustmaps.com/internal/v1/servers/search?input={Uri.EscapeDataString(host)}&onlyServersWithPlayers=true";
            var matches = await QueryRustMapsApiListAsync(client, fallbackUrl);
            if (matches != null && matches.Count > 0)
            {
                return matches.OrderBy(m => Math.Abs(m.gamePort - gamePort)).First();
            }

            fallbackUrl = $"https://api.rustmaps.com/internal/v1/servers/search?input={Uri.EscapeDataString(host)}";
            matches = await QueryRustMapsApiListAsync(client, fallbackUrl);
            if (matches != null && matches.Count > 0)
            {
                return matches.OrderBy(m => Math.Abs(m.gamePort - gamePort)).First();
            }

            return null;
        }

        private async Task<RustMapsMatch?> QueryRustMapsApiAsync(HttpClient client, string url)
        {
            try
            {
                var json = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in dataProp.EnumerateArray())
                    {
                        return new RustMapsMatch
                        {
                            name = el.TryGetProperty("name", out var n) ? n.GetString() : null,
                            mapId = el.TryGetProperty("mapId", out var m) ? m.GetString() : null,
                            ip = el.TryGetProperty("ip", out var ip) ? ip.GetString() : null,
                            gamePort = el.TryGetProperty("gamePort", out var gp) ? gp.GetInt32() : 0,
                            lastWipeUtc = el.TryGetProperty("lastWipeUtc", out var w) ? w.GetString() : null
                        };
                    }
                }
            }
            catch { }
            return null;
        }

        private async Task<List<RustMapsMatch>> QueryRustMapsApiListAsync(HttpClient client, string url)
        {
            var list = new List<RustMapsMatch>();
            try
            {
                var json = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in dataProp.EnumerateArray())
                    {
                        list.Add(new RustMapsMatch
                        {
                            name = el.TryGetProperty("name", out var n) ? n.GetString() : null,
                            mapId = el.TryGetProperty("mapId", out var m) ? m.GetString() : null,
                            ip = el.TryGetProperty("ip", out var ip) ? ip.GetString() : null,
                            gamePort = el.TryGetProperty("gamePort", out var gp) ? gp.GetInt32() : 0,
                            lastWipeUtc = el.TryGetProperty("lastWipeUtc", out var w) ? w.GetString() : null
                        });
                    }
                }
            }
            catch { }
            return list;
        }

        private void BtnOpenRustMaps_Click(object sender, RoutedEventArgs e)
        {
            var profile = _vm.Selected;
            if (profile != null && !string.IsNullOrEmpty(profile.RustMapsMapId))
            {
                var url = $"https://rustmaps.com/map/{profile.RustMapsMapId}";
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    AppendLog($"[RustMaps] Failed to open browser: {ex.Message}");
                }
            }
        }

        private async void BtnOpen3DMap_Click(object sender, RoutedEventArgs e)
        {
            if (_isMap3DActive)
            {
                CloseMap3DView();
                return;
            }

            var profile = _vm.Selected;
            bool isPlaceholder = profile != null && !string.IsNullOrEmpty(profile.LocalMapFilePath);
            if (profile == null || (!profile.IsFullConnected && !isPlaceholder))
            {
                AppendLog("[3D Map] Fully connect to a server or select an imported offline map before building a local 3D map.");
                return;
            }

            if (!SupabaseAuthManager.IsDiscordAuthenticated && !SupabaseAuthManager.IsEmailAuthenticated)
            {
                AppendLog("[3D Map] Account or Discord login required before local 3D map import.");
                return;
            }

            if (!Map3DConsentService.HasRememberedConsent())
            {
                var dialog = new Map3DConsentWindow(this);
                if (dialog.ShowDialog() != true || !dialog.Accepted)
                {
                    AppendLog("[3D Map] Local map import canceled.");
                    return;
                }

                if (dialog.Remember)
                {
                    Map3DConsentService.RememberConsent();
                }
            }

            _isMap3DPreparing = true;
            UpdateRustMapsUi();

            try
            {
                var texture = ImgMap.Source as BitmapSource;
                var references = (_monData ?? new List<(double X, double Y, string Name)>())
                    .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                    .Take(12)
                    .Select(m => new Map3DReferenceMonument(m.X, m.Y, m.Name))
                    .ToList();

                var result = await Map3DLocalBuildService.PrepareAsync(profile, texture, profile.RustMapsMapId, references, _worldSizeS, isPlaceholder ? profile.LocalMapFilePath : null);
                if (result.NeedsManualMapSelection)
                {
                    AppendLog($"[3D Map] Automatic map detection failed ({result.AttemptCount}/{result.CandidateCount} candidates tried). Asking for the map file manually.");
                    var picker = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select Rust .map file",
                        Filter = "Rust map files (*.map)|*.map|All files (*.*)|*.*",
                        InitialDirectory = Map3DLocalBuildService.GetPreferredMapPickerDirectory(),
                        CheckFileExists = true,
                        Multiselect = false
                    };

                    if (picker.ShowDialog(this) == true)
                    {
                        result = await Map3DLocalBuildService.PrepareAsync(profile, texture, profile.RustMapsMapId, references, _worldSizeS, picker.FileName);
                    }
                }

                AppendLog($"[3D Map] {result.StatusMessage} Folder: {result.FolderPath}");
                if (result.ParserReady)
                {
                    AppendLog($"[3D Map] Parser output ready for viewer. Map file: {result.MapFilePath}");
                    await OpenMap3DViewAsync(result);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[3D Map] Preparation failed: {ex.Message}");
            }
            finally
            {
                _isMap3DPreparing = false;
                UpdateRustMapsUi();
            }
        }
        private async Task OpenMap3DViewAsync(Map3DLocalBuildResult result)
        {
            _currentMapFolderPath = result.FolderPath;
            GenerateAndLoadExtraMonumentsForCurrentMap(result.FolderPath);
            await GenerateBuildingBlockedZonesForCurrentMap(result.FolderPath);
            LoadBuildingBlockedZonesForCurrentMap(result.FolderPath);
            string runtimeRoot = await PrepareMap3DViewerRuntimeAsync(result).ConfigureAwait(true);
            const string host = "rustplus3d.local";
            bool hasBuildings = System.IO.File.Exists(System.IO.Path.Combine(result.FolderPath, "map_buildings.json"));

            bool hasBlocked = System.IO.File.Exists(System.IO.Path.Combine(result.FolderPath, "building_blocked.json"));
            string url = $"https://{host}/index.html?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&mapDataUrl=/maps/current/map_data_viewer.json&embedded=1&view=3d" +
                         $"{(hasBuildings ? "&hasBuildings=1" : "")}" +
                         $"{(hasBlocked ? "&hasBlocked=1" : "")}";

            CloseMap3DView();
            _map3DWebView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _map3DWebView.NavigationCompleted += Map3DWebView_NavigationCompleted;
            Map3DHost.Children.Add(_map3DWebView);
            Map3DHost.Visibility = Visibility.Visible;
            ImgMap.Visibility = Visibility.Collapsed;

            string webViewDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustPlusDesk",
                "WebView2");
            Directory.CreateDirectory(webViewDataFolder);
            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(userDataFolder: webViewDataFolder);
            await _map3DWebView.EnsureCoreWebView2Async(webViewEnvironment);
            _map3DWebView.CoreWebView2.WebMessageReceived += Map3DWebMessageReceived;
            _map3DResourceRequestHandler = (_, args) => HandleMap3DResourceRequest(args, runtimeRoot);
            _map3DWebView.CoreWebView2.AddWebResourceRequestedFilter($"https://{host}/*", CoreWebView2WebResourceContext.All);
            _map3DWebView.CoreWebView2.WebResourceRequested += _map3DResourceRequestHandler;
            _map3DWebView.CoreWebView2.Navigate(url);

            _isMap3DActive = true;
            UpdateRustMapsUi();
        }

        private Window? _fullscreenWindow = null;

        private void ToggleWpfFullscreen()
        {
            bool isFullscreenNow = false;
            if (_fullscreenWindow == null)
            {
                if (_map3DWebView == null) return;

                // Remove WebView2 from current host
                Map3DHost.Children.Remove(_map3DWebView);

                // Create a borderless maximized window
                _fullscreenWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    WindowState = WindowState.Maximized,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(Color.FromRgb(14, 17, 23)),
                    Content = _map3DWebView
                };

                _fullscreenWindow.PreviewKeyDown += (s, ev) =>
                {
                    if (ev.Key == System.Windows.Input.Key.F11)
                    {
                        ev.Handled = true;
                        ToggleWpfFullscreen();
                    }
                };

                _fullscreenWindow.Closed += (s, args) =>
                {
                    if (_fullscreenWindow != null)
                    {
                        _fullscreenWindow = null;
                        if (_map3DWebView.Parent == null)
                        {
                            Map3DHost.Children.Add(_map3DWebView);
                        }
                    }
                    try { _map3DWebView?.CoreWebView2?.ExecuteScriptAsync("if (window.setFullscreenState) window.setFullscreenState(false);"); } catch { }
                };

                _fullscreenWindow.Show();
                isFullscreenNow = true;
            }
            else
            {
                var win = _fullscreenWindow;
                _fullscreenWindow = null;

                win.Content = null;
                win.Close();

                if (_map3DWebView != null && _map3DWebView.Parent == null)
                {
                    Map3DHost.Children.Add(_map3DWebView);
                }
                isFullscreenNow = false;
            }

            try { _map3DWebView?.CoreWebView2?.ExecuteScriptAsync($"if (window.setFullscreenState) window.setFullscreenState({isFullscreenNow.ToString().ToLower()});"); } catch { }
        }

        private void CloseMap3DView()
        {
            if (_fullscreenWindow != null)
            {
                try
                {
                    var win = _fullscreenWindow;
                    _fullscreenWindow = null;
                    win.Content = null;
                    win.Close();
                }
                catch { }
            }
            try { _map3DWebView?.CoreWebView2?.ExecuteScriptAsync("if (window.setFullscreenState) window.setFullscreenState(false);"); } catch { }
            if (_map3DWebView != null)
            {
                try
                {
                    _map3DWebView.NavigationCompleted -= Map3DWebView_NavigationCompleted;
                    if (_map3DWebView.CoreWebView2 != null)
                    {
                        _map3DWebView.CoreWebView2.WebMessageReceived -= Map3DWebMessageReceived;
                        if (_map3DResourceRequestHandler != null)
                        {
                            _map3DWebView.CoreWebView2.WebResourceRequested -= _map3DResourceRequestHandler;
                            _map3DResourceRequestHandler = null;
                        }
                        _map3DWebView.CoreWebView2.Navigate("about:blank");
                    }
                }
                catch { }
                Map3DHost.Children.Remove(_map3DWebView);
                _map3DWebView.Dispose();
                _map3DWebView = null;
            }

            Map3DHost.Visibility = Visibility.Collapsed;
            Map3DHost.Margin = new Thickness(0);
            ImgMap.Visibility = Visibility.Visible;
            _isMap3DActive = false;
            UpdateRustMapsUi();
        }

        private async void Map3DWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string? message = null;
            try { message = e.TryGetWebMessageAsString(); } catch { }

            if (message == null)
            {
                try { message = e.WebMessageAsJson; } catch { }
            }

            if (message != null)
            {
                string cleanMessage = message.Trim('"');
                if (string.Equals(cleanMessage, "toggle_fullscreen", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() =>
                    {
                        ToggleWpfFullscreen();
                    });
                    return;
                }

                if (string.Equals(cleanMessage, "close3d", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(CloseMap3DView);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(message))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(message);
                    if (doc.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        string? typeStr = typeProp.GetString();
                        if (typeStr == "save_buildings")
                        {
                            if (!string.IsNullOrEmpty(_currentMapFolderPath))
                            {
                                var dataNode = doc.RootElement.GetProperty("data");
                                var dataString = dataNode.ValueKind == System.Text.Json.JsonValueKind.String ? dataNode.GetString() : dataNode.GetRawText();
                                string path = Path.Combine(_currentMapFolderPath, "map_buildings.json");
                                await File.WriteAllTextAsync(path, dataString ?? "[]");
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void Map3DWebView_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                SyncLiveMarkersTo3DMap();
                RefreshEventDock();
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                        }
                    }
                    catch { }
                }

                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        private async Task<string> PrepareMap3DViewerRuntimeAsync(Map3DLocalBuildResult result)
        {
            string runtimeRoot = Path.Combine(RustPlusDesk.Services.Data.DataManager.AppDir, "Map3DViewer");
            Directory.CreateDirectory(runtimeRoot);
            Directory.CreateDirectory(Path.Combine(runtimeRoot, "maps", "current"));

            // Proactively clean up any legacy/unwanted directories in the viewer folder to avoid issues
            SafeDeleteDirectory(Path.Combine(runtimeRoot, ".git"));
            SafeDeleteDirectory(Path.Combine(runtimeRoot, ".agents"));
            SafeDeleteDirectory(Path.Combine(runtimeRoot, ".claude"));
            SafeDeleteDirectory(Path.Combine(runtimeRoot, "node_modules"));
            SafeDeleteDirectory(Path.Combine(runtimeRoot, "bin"));
            SafeDeleteDirectory(Path.Combine(runtimeRoot, "obj"));

            string? viewerRoot = ResolveMap3DViewerSourceRoot();
            if (viewerRoot != null) CopyDirectoryIfExists(viewerRoot, runtimeRoot);

            string? iconsRoot = ResolveIconsSourceRoot();
            if (iconsRoot != null) CopyDirectoryIfExists(iconsRoot, Path.Combine(runtimeRoot, "Icons"));

            string currentDir = Path.Combine(runtimeRoot, "maps", "current");
            CopyFileIfExists(Path.Combine(result.FolderPath, "map_resolved.json"), Path.Combine(currentDir, "map_resolved.json"));

            string targetTexturePath = Path.Combine(currentDir, "map_texture.png");
            string sourceTexturePath = Path.Combine(result.FolderPath, "map_texture.png");
            if (File.Exists(sourceTexturePath))
            {
                File.Copy(sourceTexturePath, targetTexturePath, true);
            }
            else
            {
                if (File.Exists(targetTexturePath))
                {
                    try { File.Delete(targetTexturePath); } catch { }
                }
            }

            CopyFileIfExists(Path.Combine(result.FolderPath, "map_buildings.json"), Path.Combine(currentDir, "map_buildings.json"));

            CopyFileIfExists(Path.Combine(result.FolderPath, "building_blocked.json"), Path.Combine(currentDir, "building_blocked.json"));
            await WriteViewerMapDataAsync(Path.Combine(result.FolderPath, "map_data.json"), Path.Combine(currentDir, "map_data_viewer.json"), _worldRectPx, ImgMap.Width, ImgMap.Height);
            return runtimeRoot;
        }

        private void HandleMap3DResourceRequest(CoreWebView2WebResourceRequestedEventArgs args, string runtimeRoot)
        {
            try
            {
                var uri = new Uri(args.Request.Uri);
                string relativePath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relativePath)) relativePath = "index.html";
                if (relativePath.Contains("..")) return;

                string diskPath = Path.GetFullPath(Path.Combine(runtimeRoot, relativePath));
                string root = Path.GetFullPath(runtimeRoot);
                if (diskPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(diskPath))
                {
                    byte[] bytes = File.ReadAllBytes(diskPath);
                    args.Response = CreateMap3DResponse(bytes, GetMap3DContentType(diskPath));
                    return;
                }

                bool isMapRuntimeFile = relativePath.StartsWith($"maps{Path.DirectorySeparatorChar}current{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
                bool isIconFile = relativePath.StartsWith($"Icons{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
                if (isMapRuntimeFile || isIconFile)
                {
                    if (isMapRuntimeFile)
                    {
                        args.Response = CreateMap3D404Response();
                        return;
                    }
                }

                string resourceName = "Map3DViewer/" + relativePath.Replace(Path.DirectorySeparatorChar, '/');
                byte[]? resourceBytes = ReadEmbeddedResourceBytes(resourceName);
                if (resourceBytes != null)
                {
                    args.Response = CreateMap3DResponse(resourceBytes, GetMap3DContentType(resourceName));
                    return;
                }

                args.Response = CreateMap3D404Response();
            }
            catch
            {
                // Let WebView2 surface a normal load failure for unexpected request errors.
            }
        }

        private CoreWebView2WebResourceResponse CreateMap3D404Response()
        {
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Not Found"));
            return _map3DWebView!.CoreWebView2.Environment.CreateWebResourceResponse(
                stream,
                404,
                "Not Found",
                "Content-Type: text/plain; charset=utf-8\r\nCache-Control: no-store, no-cache, must-revalidate, max-age=0\r\nPragma: no-cache\r\nExpires: 0");
        }

        private CoreWebView2WebResourceResponse CreateMap3DResponse(byte[] bytes, string contentType)
        {
            var stream = new MemoryStream(bytes);
            return _map3DWebView!.CoreWebView2.Environment.CreateWebResourceResponse(
                stream,
                200,
                "OK",
                $"Content-Type: {contentType}\r\nCache-Control: no-store, no-cache, must-revalidate, max-age=0\r\nPragma: no-cache\r\nExpires: 0");
        }

        private static byte[]? ReadEmbeddedResourceBytes(string logicalName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string? manifestName = logicalName;
            if (assembly.GetManifestResourceInfo(manifestName) == null)
            {
                Map3DResourceNameMap.Value.TryGetValue(NormalizeMap3DResourceName(logicalName), out manifestName);
            }

            if (manifestName == null) return null;
            using Stream? stream = assembly.GetManifestResourceStream(manifestName);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static string NormalizeMap3DResourceName(string name)
        {
            return name.Replace('\\', '/');
        }

        private static string GetMap3DContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".wasm" => "application/wasm",
                ".obj" => "text/plain; charset=utf-8",
                ".mtl" => "text/plain; charset=utf-8",
                ".glb" => "model/gltf-binary",
                ".gltf" => "model/gltf+json",
                _ => "application/octet-stream"
            };
        }

        private static string? ResolveIconsSourceRoot()
        {
            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "icons")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "RustPlusDesktop", "Assets", "icons")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "RustPlusDesktop", "RustPlusDesktop", "Assets", "icons")),
                Path.Combine(baseDir, "MapParser", "Icons"),
                Path.Combine(baseDir, "Assets", "icons")
            };

            return candidates.FirstOrDefault(path =>
                Directory.Exists(path) &&
                (File.Exists(Path.Combine(path, "airfield.png")) || File.Exists(Path.Combine(path, "trainyard.png"))));
        }
        private static string? ResolveMap3DViewerSourceRoot()
        {
            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "MapParser"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "MapParser")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "MapParser"))
            };

            string? found = candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "index.html")) && File.Exists(Path.Combine(p, "app.js")));
            return found;
        }

        private static async Task WriteViewerMapDataAsync(string sourcePath, string targetPath, Rect worldRectPx, double imageWidth, double imageHeight)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("map_data.json was not found.", sourcePath);
            var node = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath).ConfigureAwait(false)) as JsonObject;
            if (node == null) throw new InvalidDataException("map_data.json root must be an object.");

            string parentDir = Path.GetDirectoryName(targetPath) ?? "";
            bool hasTexture = File.Exists(Path.Combine(parentDir, "map_texture.png"));
            node["mapTextureSource"] = hasTexture ? "/maps/current/map_texture.png" : null;

            // Read worldSize from the parsed map_data.json (written by MapParser as "size").
            // This is critical for offline/placeholder maps where _worldSizeS is 0 because
            // there is no API connection – without the correct size the texture UV is wrong.
            int parsedWorldSize = 0;
            if (node.TryGetPropertyValue("size", out var sizeNode) && sizeNode != null)
                int.TryParse(sizeNode.ToJsonString(), out parsedWorldSize);

            if (parsedWorldSize > 0 && imageWidth > 0 && imageHeight > 0)
            {
                // Recompute the world rect directly from the map's own size field so the UV
                // is always correct, regardless of whether _worldSizeS was available.
                worldRectPx = ComputeWorldRectFromWorldSize(imageWidth, imageHeight, parsedWorldSize);
            }

            node["mapTexturePaddingWorld"] = 2000;
            node["mapTextureAutoAlign"] = true;
            if (imageWidth > 0 && imageHeight > 0 && worldRectPx.Width > 0 && worldRectPx.Height > 0)
            {
                node["mapTextureUv"] = new JsonObject
                {
                    ["offsetU"] = worldRectPx.X / imageWidth,
                    ["offsetV"] = worldRectPx.Y / imageHeight,
                    ["repeatU"] = worldRectPx.Width / imageWidth,
                    ["repeatV"] = worldRectPx.Height / imageHeight
                };
                node["mapTextureUvZoom"] = 1.0;
            }
            await File.WriteAllTextAsync(targetPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })).ConfigureAwait(false);
        }

        private string ImageSourceToBase64(ImageSource source)
        {
            if (source is BitmapImage bmp)
            {
                try
                {
                    using var ms = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    encoder.Save(ms);
                    var bytes = ms.ToArray();
                    return "data:image/png;base64," + Convert.ToBase64String(bytes);
                }
                catch { }
            }
            return "";
        }

        private async void SyncLiveMarkersTo3DMap()
        {
            if (!_isMap3DActive || _map3DWebView?.CoreWebView2 == null) return;

            try
            {
                var playersList = new List<object>();
                string mySteamIdStr = TrackingService.SteamId64;
                ulong mySteamId = 0;
                ulong.TryParse(mySteamIdStr, out mySteamId);

                string myAvatarB64 = "";

                // Add Team Members
                foreach (var kv in _dynEls.ToList())
                {
                    if (kv.Value is FrameworkElement el && el.Tag is PlayerMarkerTag tag)
                    {
                        if (tag.SteamId == 0 || tag.IsDeathPin) continue;
                        var sid = tag.SteamId;
                        var vm = TeamMembers.FirstOrDefault(t => t.SteamId == sid);
                        var name = vm?.Name ?? "player";
                        var avatarUrl = vm?.Avatar != null ? ImageSourceToBase64(vm.Avatar) : "";

                        if (sid == mySteamId && !string.IsNullOrEmpty(avatarUrl))
                        {
                            myAvatarB64 = avatarUrl;
                        }

                        bool online = false;
                        bool dead = false;
                        if (_lastPresence.TryGetValue(sid, out var p))
                        {
                            online = p.Item1;
                            dead = p.Item2;
                        }

                        // Determine position. C# side receives world coordinates which MapParser uses as x/y
                        double x = 0, y = 0;
                        if (_lastPlayersBySid.TryGetValue(sid, out var pos))
                        {
                            x = pos.x;
                            y = pos.y;
                        }

                        playersList.Add(new
                        {
                            sid,
                            name,
                            avatar = avatarUrl,
                            x,
                            y,
                            online,
                            dead,
                            isSelf = (sid == mySteamId)
                        });
                    }
                }

                var deathsList = new List<object>();

                // Add Death Markers

                if (_vm?.Selected?.DeathMarkers != null)
                {
                    foreach (var m in _vm.Selected.DeathMarkers)
                    {
                        deathsList.Add(new
                        {
                            name = m.CustomName ?? "Death",
                            x = m.X,
                            y = m.Y,
                            avatar = myAvatarB64,
                            isSelf = true
                        });
                    }
                }

                // Cargo ship markers (Type 5) — forward id, world-coords and heading to the 3D viewer
                var cargoList = new List<object>();
                foreach (var m in (_lastDynMarkers ?? []).Where(m => m.Type == 5))
                {
                    cargoList.Add(new
                    {
                        id = m.Id,
                        x = m.X,
                        y = m.Y,
                        rotation = m.Rotation   // degrees, Rust convention (0 = north, CW)
                    });
                }

                // Travelling vendor markers (Type 6) — direction is derived in 3D from movement between x/y updates
                var vendorList = new List<object>();
                foreach (var m in (_lastDynMarkers ?? []).Where(m => m.Type == 6))
                {
                    vendorList.Add(new
                    {
                        id = m.Id,
                        x = m.X,
                        y = m.Y
                    });
                }

                // Patrol helicopter markers (Type 8) — direction is derived in 3D from movement between x/y updates
                var patrolHeliList = new List<object>();
                foreach (var m in (_lastDynMarkers ?? []).Where(m => m.Type == 8))
                {
                    patrolHeliList.Add(new
                    {
                        id = m.Id,
                        x = m.X,
                        y = m.Y
                    });
                }

                // Chinook helicopter markers (Type 4) — direction is derived in 3D from movement between x/y updates
                var chinookList = new List<object>();
                foreach (var m in (_lastDynMarkers ?? []).Where(m => m.Type == 4))
                {
                    chinookList.Add(new
                    {
                        id = m.Id,
                        x = m.X,
                        y = m.Y
                    });
                }

                var liveData = new { players = playersList, deaths = deathsList };
                string liveJson = JsonSerializer.Serialize(liveData);
                string cargoJson = JsonSerializer.Serialize(cargoList);
                string vendorJson = JsonSerializer.Serialize(vendorList);
                string patrolHeliJson = JsonSerializer.Serialize(patrolHeliList);
                string chinookJson = JsonSerializer.Serialize(chinookList);

                string script = $$"""
                    if (window.updateLiveMarkers) window.updateLiveMarkers({{liveJson}}.players, {{liveJson}}.deaths);
                    if (window.updateCargoMarkers) window.updateCargoMarkers({{cargoJson}});
                    if (window.updateVendorMarkers) window.updateVendorMarkers({{vendorJson}});
                    if (window.updatePatrolHeliMarkers) window.updatePatrolHeliMarkers({{patrolHeliJson}});
                    if (window.updateChinookMarkers) window.updateChinookMarkers({{chinookJson}});
                    """;
                await _map3DWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch { }
        }

        private static void CopyFileIfExists(string source, string target)
        {
            if (File.Exists(source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        private static bool IsIgnoredRuntimePath(string relativePath)
        {
            var segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (segment.StartsWith('.') ||
                    string.Equals(segment, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(segment, "maps", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CopyDirectoryIfExists(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir)) return;
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, sourceFile);
                if (IsIgnoredRuntimePath(relative)) continue;

                string target = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(sourceFile, target, overwrite: true);
            }
        }
        private async void BtnRefetchRustMaps_Click(object sender, RoutedEventArgs e)
        {
            await SearchRustMapsAsync(forceRefetch: true);
        }

        private string? _currentActiveHeatmap = null;

        private void BtnToggleHeatmap_Click(object sender, RoutedEventArgs e)
        {
            if (HeatmapPopup != null)
            {
                HeatmapPopup.IsOpen = !HeatmapPopup.IsOpen;
                if (HeatmapPopup.IsOpen)
                {
                    UpdateBentoActiveStates();
                }
            }
        }

        private static readonly Dictionary<string, string> HeatmapLabels = new()
        {
            { "ores", "Ores" }, { "wood", "Wood Piles" }, { "logs", "Log Piles" },
            { "mushroom", "Mushrooms" }, { "berries", "Berries" }, { "corn", "Corn" },
            { "pumpkin", "Pumpkins" }, { "potato", "Potatoes" }, { "wheat", "Wheat" },
            { "bear", "Bears" }, { "boar", "Boars" }, { "chicken", "Chickens" },
            { "wolf", "Wolves" }, { "stag", "Deers" }, { "crocodile", "Crocodiles" },
            { "tiger", "Tigers" }, { "snake", "Snakes" },
            { "junkpiles", "Junkpiles" }, { "rowboat", "Rowboats" },
            { "modularcar", "Modular Cars" }, { "horse", "Horses" },
            { "pedalbike", "Bicycles" }, { "hab", "Hot Air Balloons" },
            { "flowers", "Flowers" },
        };

        private void UpdateBentoActiveStates()
        {
            string[] allCategories = { "ores", "wood", "logs", "mushroom", "berries", "corn", "pumpkin", "potato", "wheat", "bear", "boar", "chicken", "wolf", "stag", "crocodile", "tiger", "snake", "junkpiles", "rowboat", "modularcar", "horse", "pedalbike", "hab", "flowers" };
            foreach (var cat in allCategories)
            {
                var border = FindName("Bento_" + cat) as System.Windows.Controls.Border;
                if (border != null)
                {
                    bool isActive = (cat == _currentActiveHeatmap);
                    border.BorderBrush = isActive
                        ? new SolidColorBrush(Color.FromRgb(74, 193, 255))
                        : Brushes.Transparent;
                    border.BorderThickness = isActive ? new Thickness(1.5) : new Thickness(1);
                }
            }

            // Update active heatmap badge
            var badge = FindName("ActiveHeatmapBadge") as Badge;
            var label = FindName("ActiveHeatmapLabel") as System.Windows.Controls.TextBlock;
            bool hasActive = _currentActiveHeatmap != null && HeatmapLabels.ContainsKey(_currentActiveHeatmap);
            if (badge != null && label != null)
            {
                badge.Visibility = hasActive ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                label.Text = hasActive
                    ? $"Active: {HeatmapLabels[_currentActiveHeatmap!]}"
                    : "No heatmap active";
            }

            var glow = FindName("HeatmapActiveGlow") as System.Windows.Controls.Border;
            if (glow != null)
                glow.Visibility = hasActive ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void HeatmapSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var searchBox = sender as System.Windows.Controls.TextBox;
            if (searchBox == null) return;

            string filter = searchBox.Text?.Trim().ToLowerInvariant() ?? "";

            string[] allCategories = { "ores", "wood", "logs", "mushroom", "berries", "corn", "pumpkin", "potato", "wheat", "bear", "boar", "chicken", "wolf", "stag", "crocodile", "tiger", "snake", "junkpiles", "rowboat", "modularcar", "horse", "rose", "orchid", "sunflower" };

            foreach (var cat in allCategories)
            {
                var border = FindName("Bento_" + cat) as System.Windows.Controls.Border;
                if (border == null) continue;

                if (string.IsNullOrEmpty(filter))
                {
                    border.Visibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    var btn = border.Child as System.Windows.Controls.Button;
                    string tagText = btn?.Tag?.ToString()?.ToLowerInvariant() ?? "";
                    string toolTipText = btn?.ToolTip?.ToString()?.ToLowerInvariant() ?? "";
                    border.Visibility = (tagText.Contains(filter) || toolTipText.Contains(filter))
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed;
                }
            }
        }

        private async void BtnHeatmapIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement btn && btn.Tag is string heatmapType)
            {
                // Toggle behavior: if they click the active one, clear it
                if (_currentActiveHeatmap == heatmapType)
                {
                    heatmapType = "clear";
                }

                _currentActiveHeatmap = (heatmapType == "clear") ? null : heatmapType;
                UpdateBentoActiveStates();

                if (heatmapType == "clear")
                {
                    ImgHeatmap.Source = null;
                    if (_isMap3DActive && _map3DWebView?.CoreWebView2 != null)
                    {
                        var data = new { type = "CLEAR_HEATMAP" };
                        string json = JsonSerializer.Serialize(data);
                        _ = _map3DWebView.CoreWebView2.ExecuteScriptAsync($"if (window.handleHeatmapRequest) window.handleHeatmapRequest({json});");
                    }
                    return;
                }

                if (_isMap3DActive && _map3DWebView?.CoreWebView2 != null)
                {
                    try
                    {
                        var data = new
                        {
                            type = "SHOW_HEATMAP",
                            category = heatmapType
                        };

                        string json = JsonSerializer.Serialize(data);
                        await _map3DWebView.CoreWebView2.ExecuteScriptAsync($"if (window.handleHeatmapRequest) window.handleHeatmapRequest({json});");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[Heatmap] Error sending to viewer: {ex.Message}");
                    }
                }
                else
                {
                    await DrawHeatmapOn2DMapAsync(heatmapType);
                }
            }
        }

        private async Task DrawHeatmapOn2DMapAsync(string category)
        {
            var profile = _vm.Selected;
            if (profile == null) return;

            string folderPath = Map3DLocalBuildService.GetPreparedFolderPath(profile, profile.RustMapsMapId);
            string dataPath = System.IO.Path.Combine(folderPath, "map_data.json");
            if (!System.IO.File.Exists(dataPath))
            {
                AppendLog("[Heatmap] No map data found for 2D map. Try building 3D Map first.");
                return;
            }

            try
            {
                using var fs = new System.IO.FileStream(dataPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(fs);

                if (doc.RootElement.TryGetProperty("heatmaps", out var heatmapsEl) &&
                    heatmapsEl.TryGetProperty(category, out var b64El))
                {
                    string b64 = b64El.GetString() ?? "";
                    if (string.IsNullOrEmpty(b64)) return;

                    byte[] rawData = Convert.FromBase64String(b64);
                    int width = 512;
                    int height = 512;
                    if (rawData.Length != width * height) return;

                    int[] pixels = new int[width * height];
                    float[] blurred = new float[width * height];
                    int radius = 3; // 7x7 blur

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            float sum = 0;
                            int count = 0;
                            for (int dy = -radius; dy <= radius; dy++)
                            {
                                int ny = y + dy;
                                if (ny < 0 || ny >= height) continue;
                                for (int dx = -radius; dx <= radius; dx++)
                                {
                                    int nx = x + dx;
                                    if (nx < 0 || nx >= width) continue;
                                    sum += rawData[ny * width + nx];
                                    count++;
                                }
                            }
                            blurred[y * width + x] = sum / count;
                        }
                    }

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int destI = y * width + x;
                            float original = rawData[destI];
                            float val = Math.Max(blurred[destI], original);

                            if (val <= 2)
                            {
                                pixels[destI] = 0;
                                continue;
                            }

                            float t = Math.Min(1.0f, val / 255f);
                            byte a = (byte)(t * 180 + 75);
                            byte r = (byte)Math.Min(255, 255 * (t * 2));
                            byte g = (byte)Math.Min(255, 255 * (2 - t * 2));
                            byte b = 0;

                            // Pre-multiply alpha for Pbgra32
                            r = (byte)((r * a) / 255);
                            g = (byte)((g * a) / 255);
                            b = (byte)((b * a) / 255);

                            pixels[destI] = (a << 24) | (r << 16) | (g << 8) | b;
                        }
                    }

                    // Apply the scale and margin
                    var ptTopLeft = WorldToImagePx(0, _worldSizeS);
                    var ptBotRight = WorldToImagePx(_worldSizeS, 0);
                    if (ptBotRight.X > ptTopLeft.X && ptBotRight.Y > ptTopLeft.Y)
                    {
                        ImgHeatmap.Width = ptBotRight.X - ptTopLeft.X;
                        ImgHeatmap.Height = ptBotRight.Y - ptTopLeft.Y;
                        ImgHeatmap.Margin = new Thickness(ptTopLeft.X, ptTopLeft.Y, 0, 0);
                    }

                    var writeableBmp = new System.Windows.Media.Imaging.WriteableBitmap(
                        width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);

                    writeableBmp.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);

                    ImgHeatmap.Source = writeableBmp;
                }
                else
                {
                    AppendLog($"[Heatmap] Category '{category}' not found in map data.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Heatmap] Failed to draw 2D heatmap: {ex.Message}");
            }
        }

        public void CheckAndExecutePendingMapCopy(ServerProfile connectedProfile)
        {
            if (_copyMapSourceProfile == null) return;

            var sourceProfile = _copyMapSourceProfile;
            if (sourceProfile == connectedProfile) return;

            try
            {
                AppendLog($"[Offline Map] Checking layout match between offline map '{sourceProfile.Name}' and connected server '{connectedProfile.Name}'...");

                string sourceFolder = Map3DLocalBuildService.GetPreparedFolderPath(sourceProfile, sourceProfile.RustMapsMapId);
                string resolvedPath = Path.Combine(sourceFolder, "map_resolved.json");

                if (!File.Exists(resolvedPath))
                {
                    AppendLog($"[Offline Map] Source map '{sourceProfile.Name}' has not been parsed into 3D map data yet. Please open its 3D map once to parse it.");
                    System.Windows.MessageBox.Show($"The offline map '{sourceProfile.Name}' has not been parsed yet. Please open its 3D Map once to parse it, then try copying again.", "Copy Map Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    _copyMapSourceProfile = null;
                    return;
                }

                var references = (_monData ?? new List<(double X, double Y, string Name)>())
                    .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                    .Take(12)
                    .Select(m => new Map3DReferenceMonument(m.X, m.Y, m.Name))
                    .ToList();

                var score = Map3DLocalBuildService.ScoreParsedMap(resolvedPath, references, _worldSizeS);
                bool isGoodMatch = Map3DLocalBuildService.IsGoodMatch(score, references);

                if (!isGoodMatch)
                {
                    AppendLog($"[Offline Map] Layout mismatch! Mapped count: {score.MatchedCount}, distance: {score.TotalDistance}. Aborting copy.");
                    System.Windows.MessageBox.Show($"Map layouts do not match! The map '{sourceProfile.Name}' does not appear to be the same map as the connected server '{connectedProfile.Name}'.", "Map Mismatch", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    _copyMapSourceProfile = null;
                    return;
                }

                string targetFolder = Map3DLocalBuildService.GetPreparedFolderPath(connectedProfile, connectedProfile.RustMapsMapId);
                Directory.CreateDirectory(targetFolder);

                foreach (var file in Directory.GetFiles(sourceFolder))
                {
                    string destFile = Path.Combine(targetFolder, Path.GetFileName(file));
                    File.Copy(file, destFile, overwrite: true);
                }

                connectedProfile.LocalMapFilePath = sourceProfile.LocalMapFilePath;
                connectedProfile.LocalMapImagePath = sourceProfile.LocalMapImagePath;
                _vm.Save();

                AppendLog($"[Offline Map] Map successfully copied from '{sourceProfile.Name}' to '{connectedProfile.Name}'!");
                System.Windows.MessageBox.Show($"Successfully copied 3D map and local assets from '{sourceProfile.Name}' to '{connectedProfile.Name}'!", "Map Copied", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"[Offline Map] Error during map copy: {ex.Message}");
                System.Windows.MessageBox.Show($"An error occurred while copying the map: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                _copyMapSourceProfile = null;
                UpdateRustMapsUi();
            }
        }

        private sealed class RustMapsMatch
        {
            public string? name { get; set; }
            public string? mapId { get; set; }
            public string? ip { get; set; }
            public int gamePort { get; set; }
            public string? lastWipeUtc { get; set; }
        }
    }
}
