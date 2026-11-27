using RustPlusDesk.Services.Camera;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RustPlusDesk.Views
{
    /// <summary>
    /// The players the cameras have seen by name, newest first: which camera, when, and for how long. Filtered to the
    /// selected server unless "all servers" is ticked. Refreshes while open.
    /// </summary>
    public partial class SeenPlayersWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly string? _server;
        private readonly string? _cameraId;
        private readonly Func<string, string> _cameraName;

        private sealed record Row(string Name, string TeamLabel, Brush NameBrush, string Camera, string When, string Duration, string Server);

        /// <param name="cameraId">Only this camera's sightings (the name tag on a camera card), or all cameras.</param>
        public SeenPlayersWindow(string? server, Func<string, string> cameraName, string? cameraId = null)
        {
            InitializeComponent();
            _server = server;
            _cameraId = cameraId;
            _cameraName = cameraName;
            if (cameraId != null) Title = Properties.Resources.GetString("SeenPlayers") + " · " + cameraName(cameraId);
            ChkAllServers.IsChecked = string.IsNullOrEmpty(server);
            ChkAllServers.IsEnabled = !string.IsNullOrEmpty(server);
            ChkAllServers.Checked += (_, __) => Refresh();
            ChkAllServers.Unchecked += (_, __) => Refresh();
            BtnClose.Click += (_, __) => Close();
            BtnClear.Click += (_, __) => ClearSightings();
            CameraSeenPlayers.Changed += OnChanged;
            Closed += (_, __) => CameraSeenPlayers.Changed -= OnChanged;
            Refresh();
        }

        private void OnChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

        private bool AllServers => ChkAllServers.IsChecked == true || string.IsNullOrEmpty(_server);

        private void Refresh()
        {
            var all = AllServers;
            var team = (Brush)new SolidColorBrush(Color.FromRgb(0x5C, 0xD6, 0x6B));
            var other = (Brush)(TryFindResource("TextPrimary") as Brush ?? Brushes.White);
            var teamLabel = Properties.Resources.GetString("SeenPlayersTeam");
            var rows = CameraSeenPlayers.Snapshot(all ? null : _server)
                .Where(s => _cameraId == null || s.CameraId == _cameraId)
                .Select(s => new Row(
                    s.Name,
                    s.Team ? teamLabel : "",
                    s.Team ? team : other,
                    _cameraName(s.CameraId),
                    s.FirstSeenUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    FormatDuration(s.LastSeenUtc - s.FirstSeenUtc),
                    s.Server))
                .ToList();
            List.ItemsSource = rows;
            TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ColServer.Width = all ? 160 : 0;
        }

        private static string FormatDuration(TimeSpan d)
        {
            if (d.TotalSeconds < 60) return "< 1 min";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min";
            return $"{(int)d.TotalHours} h {d.Minutes:00} min";
        }

        private void ClearSightings()
        {
            var text = AllServers
                ? Properties.Resources.GetString("SeenPlayersClearAllConfirm")
                : Properties.Resources.GetString("SeenPlayersClearConfirm");
            if (MessageBox.Show(this, text, Properties.Resources.GetString("SeenPlayers"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            CameraSeenPlayers.Clear(AllServers ? null : _server, _cameraId);
            Refresh();
        }
    }
}
