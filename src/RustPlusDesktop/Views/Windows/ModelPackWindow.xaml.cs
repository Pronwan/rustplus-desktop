using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using RustPlusDesk.Services.ModelPacks;
using Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    /// <summary>
    /// Offers the model packs a feature needs and shows their download. The download belongs to
    /// <see cref="ModelPackService"/>, so this window can step aside and leave it running.
    /// </summary>
    public partial class ModelPackWindow : FluentWindow
    {
        private readonly List<ToggleSwitch> _switches = new();
        private bool _closed;

        /// <summary>True when the user sent the download to the background.</summary>
        public bool MovedToBackground { get; private set; }

        public ModelPackWindow(string heading, string message, IEnumerable<ModelPackStatus> packs) : this()
        {
            TxtHeading.Text = heading;
            TxtMessage.Text = message;

            foreach (ModelPackStatus pack in packs)
            {
                bool needed = pack.MissingFiles > 0;
                var toggle = new ToggleSwitch { IsChecked = needed, IsEnabled = needed, Tag = pack.Id };
                _switches.Add(toggle);

                var card = new CardControl
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    Icon = new SymbolIcon(IconOf(pack.Id)),
                    Header = Describe(pack),
                    Content = toggle
                };
                card.Click += (_, _) => { if (needed) toggle.IsChecked = toggle.IsChecked != true; };
                PackList.Children.Add(card);
            }

            if (_switches.All(toggle => !toggle.IsEnabled))
            {
                BtnDownload.Visibility = Visibility.Collapsed;
                BtnCancel.Content = "Close";
            }
        }

        private ModelPackWindow()
        {
            InitializeComponent();
            ModelPackService.Instance.DownloadChanged += OnDownloadChanged;
            Closed += (_, _) =>
            {
                _closed = true;
                ModelPackService.Instance.DownloadChanged -= OnDownloadChanged;
            };
        }

        /// <summary>A window onto the download already running, opened from the title bar.</summary>
        public static ModelPackWindow ForRunningDownload()
        {
            var window = new ModelPackWindow();
            window.TxtHeading.Text = "Downloading 3D models";
            window.TxtMessage.Text = "You can keep using the app while this runs.";
            window.ShowDownloading();
            window.ShowProgress();
            return window;
        }

        private static SymbolRegular IconOf(string pack) => pack switch
        {
            "builder" => SymbolRegular.Wrench24,
            "monuments" => SymbolRegular.BuildingMultiple24,
            "terrain" => SymbolRegular.Earth24,
            _ => SymbolRegular.Cube24
        };

        private System.Windows.Controls.StackPanel Describe(ModelPackStatus pack)
        {
            string size = FormatBytes(pack.Installed ? pack.MissingBytes : pack.PackBytes);
            string title = !pack.Installed ? $"{pack.Title} · {size}"
                : pack.MissingFiles > 0 ? $"{pack.Title} update · {size}"
                : $"{pack.Title} · Installed";
            return new System.Windows.Controls.StackPanel
            {
                Children =
                {
                    new Wpf.Ui.Controls.TextBlock
                    {
                        Text = title,
                        FontTypography = FontTypography.BodyStrong
                    },
                    new Wpf.Ui.Controls.TextBlock
                    {
                        Text = pack.Description,
                        FontTypography = FontTypography.Caption,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (System.Windows.Media.Brush)FindResource("TextSubtle")
                    }
                }
            };
        }

        internal static string FormatBytes(long bytes) => $"{bytes / 1048576.0:0.#} MB";

        private void OnDownloadChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(ShowProgress));

        private void ShowProgress()
        {
            if (_closed) return;
            ModelPackService service = ModelPackService.Instance;
            if (service.CurrentProgress is { } update)
            {
                BarProgress.Value = update.TotalBytes > 0 ? update.DoneBytes * 100.0 / update.TotalBytes : 0;
                TxtProgress.Text = $"{FormatBytes(update.DoneBytes)} of {FormatBytes(update.TotalBytes)}";
            }
            else if (_switches.Count == 0 && !service.IsDownloading) Close();
        }

        private void ShowDownloading()
        {
            PackList.IsEnabled = false;
            BtnDownload.Visibility = Visibility.Collapsed;
            BtnBackground.Visibility = Visibility.Visible;
            BtnCancel.Content = "Cancel";
            InfoError.IsOpen = false;
            ProgressArea.Visibility = Visibility.Visible;
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            var selected = _switches.Where(toggle => toggle.IsChecked == true).Select(toggle => (string)toggle.Tag).ToList();
            if (selected.Count == 0)
            {
                DialogResult = false;
                return;
            }

            ShowDownloading();
            try
            {
                await ModelPackService.Instance.DownloadAsync(selected);
                if (!_closed) DialogResult = true;
            }
            catch (OperationCanceledException)
            {
                if (!_closed) DialogResult = false;
            }
            catch (Exception ex)
            {
                if (_closed) return;
                PackList.IsEnabled = true;
                BtnDownload.Visibility = Visibility.Visible;
                BtnDownload.Content = "Try again";
                BtnBackground.Visibility = Visibility.Collapsed;
                BtnCancel.Content = "Not now";
                ProgressArea.Visibility = Visibility.Collapsed;
                InfoError.Message = ex.Message;
                InfoError.IsOpen = true;
            }
        }

        private void BtnBackground_Click(object sender, RoutedEventArgs e)
        {
            MovedToBackground = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (ModelPackService.Instance.IsDownloading && BtnBackground.Visibility == Visibility.Visible)
            {
                BtnCancel.IsEnabled = false;
                ModelPackService.Instance.CancelDownload();
            }
            else if (_switches.Count > 0) DialogResult = false;
            else Close();
        }
    }
}
