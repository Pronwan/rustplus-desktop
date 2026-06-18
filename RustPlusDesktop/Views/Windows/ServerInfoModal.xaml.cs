using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using RustPlusDesk.Models;
using RustPlusDesk.ViewModels;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class ServerInfoModal : Window
    {
        private readonly ServerProfile _profile;
        private readonly MainViewModel _vm;
        private readonly IRustPlusClient? _rustClient;
        private string? _websiteUrl;
        private bool _isShowingMessageBox = false;

        public ServerInfoModal(ServerProfile profile, MainViewModel vm, IRustPlusClient? rustClient)
        {
            InitializeComponent();
            _profile = profile;
            _vm = vm;
            _rustClient = rustClient;

            Title = _profile.Name;
            TxtName.Text = _profile.Name;
            TxtAddress.Text = $"{_profile.Host}:{_profile.Port}";

            // Pre-fill from VM if connected to the current server
            if (_vm.Selected == _profile)
            {
                TxtPlayers.Text = _vm.ServerPlayers ?? "-/-";
                TxtQueue.Text = _vm.ServerQueue ?? "-";
                TxtWipe.Text = _vm.ServerWipe ?? "-";
            }
            else
            {
                TxtPlayers.Text = "-/-";
                TxtQueue.Text = "-";
                TxtWipe.Text = "-";
            }

            TxtDescription.Text = string.IsNullOrWhiteSpace(_profile.Description)
                ? "No detailed description available for this server."
                : _profile.Description;

            // Apply WPF-UI Theme
            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
            }
            catch { }

            Loaded += ServerInfoModal_Loaded;
        }

        private async void ServerInfoModal_Loaded(object sender, RoutedEventArgs e)
        {
            PnlLoading.Visibility = Visibility.Visible;
            PnlContent.Visibility = Visibility.Collapsed;

            try
            {
                await FetchDetailsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching server details: {ex.Message}");
            }
            finally
            {
                PnlLoading.Visibility = Visibility.Collapsed;
                PnlContent.Visibility = Visibility.Visible;
            }
        }

        private async Task FetchDetailsAsync()
        {
            if (_rustClient == null)
            {
                TxtDescription.Text = "No active connection to retrieve server info.";
                return;
            }

            try
            {
                var info = await _rustClient.GetServerInfoAsync();
                if (info != null)
                {
                    if (!string.IsNullOrWhiteSpace(info.Name))
                    {
                        TxtName.Text = info.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(info.HeaderImage))
                    {
                        DisplayBanner(info.HeaderImage);
                    }
                    else
                    {
                        ImgBanner.Visibility = Visibility.Collapsed;
                    }

                    if (info.PlayerCount.HasValue && info.MaxPlayerCount.HasValue)
                    {
                        TxtPlayers.Text = $"{info.PlayerCount.Value}/{info.MaxPlayerCount.Value}";
                    }

                    if (info.QueuedPlayerCount.HasValue)
                    {
                        TxtQueue.Text = info.QueuedPlayerCount.Value.ToString();
                    }

                    if (info.WipeTime.HasValue)
                    {
                        TxtWipe.Text = info.WipeTime.Value.ToLocalTime().ToString("g");
                    }

                    _websiteUrl = info.Url;
                    BtnWebsite.Visibility = string.IsNullOrWhiteSpace(_websiteUrl) ? Visibility.Collapsed : Visibility.Visible;

                    if (!string.IsNullOrWhiteSpace(_websiteUrl))
                    {
                        TxtDescription.Text = $"The Rust+ API does not provide a detailed description for this server.\n\nYou can find more information on the server's official website:\n{_websiteUrl}";
                    }
                    else
                    {
                        TxtDescription.Text = "The Rust+ API does not provide a detailed description for this server.";
                    }
                }
                else
                {
                    TxtDescription.Text = "Could not retrieve details from the Rust+ connection. Make sure you are connected to the server.";
                }
            }
            catch (Exception ex)
            {
                TxtDescription.Text = $"Failed to load server details: {ex.Message}";
            }
        }

        private void DisplayBanner(string url)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(url);
                bmp.EndInit();
                ImgBanner.Source = bmp;
                ImgBanner.Visibility = Visibility.Visible;
            }
            catch
            {
                ImgBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnCopyAddress_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText($"{_profile.Host}:{_profile.Port}");
                _isShowingMessageBox = true;
                MessageBox.Show("Server address copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
            finally
            {
                _isShowingMessageBox = false;
            }
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_websiteUrl)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_websiteUrl) { UseShellExecute = true });
            }
            catch { }
        }

        // Allow dragging the window
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }
    }
}
