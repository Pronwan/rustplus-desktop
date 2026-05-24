using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Models;

namespace RustPlusDesk.Views
{
    public partial class MainWindow
    {
        private bool _isRustMapsSearching;

        public void UpdateRustMapsUi()
        {
            var profile = _vm.Selected;
            if (profile == null || (!profile.IsConnected && !profile.IsFullConnected))
            {
                RustMapsOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            RustMapsOverlay.Visibility = Visibility.Visible;

            if (_isRustMapsSearching)
            {
                TxtRustMapsStatus.Text = "Searching...";
                BtnOpenRustMaps.IsEnabled = false;
            }
            else if (!string.IsNullOrEmpty(profile.RustMapsMapId))
            {
                TxtRustMapsStatus.Text = "RustMaps ↗";
                BtnOpenRustMaps.IsEnabled = true;
            }
            else
            {
                TxtRustMapsStatus.Text = "No Map Found";
                BtnOpenRustMaps.IsEnabled = false;
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

            // 1. If we already have a Map ID and are NOT forcing a refetch, show UI immediately and check wipe time silently in background
            if (!forceRefetch && !string.IsNullOrEmpty(profile.RustMapsMapId))
            {
                _isRustMapsSearching = false;
                UpdateRustMapsUi();

                // Silently query RustMaps in background to check for wipes
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var match = await FetchRustMapsServerMatchAsync(profile.Host, profile.Port);
                        if (match != null)
                        {
                            if (DateTime.TryParse(match.lastWipeUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime lastWipe))
                            {
                                bool isNewerWipe = profile.RustMapsFetchTime.HasValue && lastWipe > profile.RustMapsFetchTime.Value;
                                bool isDifferentMap = match.mapId != profile.RustMapsMapId;

                                if (isNewerWipe || isDifferentMap)
                                {
                                    profile.RustMapsMapId = match.mapId;
                                    profile.RustMapsWipeTime = lastWipe;
                                    profile.RustMapsFetchTime = DateTime.UtcNow;
                                    _vm.Save();

                                    await Dispatcher.InvokeAsync(() => UpdateRustMapsUi());
                                    AppendLog($"[RustMaps] Detected server wipe/change! Updated map ID to {match.mapId}.");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[RustMaps] Background wipe check error: {ex.Message}");
                    }
                });

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

        private async void BtnRefetchRustMaps_Click(object sender, RoutedEventArgs e)
        {
            await SearchRustMapsAsync(forceRefetch: true);
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
