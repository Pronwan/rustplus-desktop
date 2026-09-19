using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// Cloud 24/7: which servers stay watched while this app is closed.
    ///
    /// Its job is honesty rather than density. Somebody who believes a server is
    /// covered when it is not will discover the mistake during a raid, so the three
    /// things that can differ — enrolled, live, and who holds the connection — are
    /// shown separately instead of collapsed into a single hopeful "on".
    ///
    /// Reads and writes the same <c>me/cloud-sessions</c> contract as the web
    /// dashboard, so the two surfaces cannot disagree about what is covered.
    /// </summary>
    public partial class Cloud24x7Window : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<ServerRow> _rows = new();
        private CloudSessionsApi.CloudPlan? _plan;

        public Cloud24x7Window()
        {
            InitializeComponent();
            ServerList.ItemsSource = _rows;
            Loaded += async (_, _) => await RefreshAsync();
        }

        /// <summary>One server as the list shows it.</summary>
        public sealed class ServerRow
        {
            public string UserServerId { get; init; } = string.Empty;

            public string? ServerKey { get; init; }

            public string Name { get; init; } = string.Empty;

            public bool Enrolled { get; init; }

            public bool CanToggle { get; init; }

            public string OwnerText { get; init; } = string.Empty;

            public Brush OwnerBrush { get; init; } = Brushes.Gray;

            public string DetailText { get; init; } = string.Empty;

            public Visibility DetailVisibility
                => string.IsNullOrWhiteSpace(DetailText) ? Visibility.Collapsed : Visibility.Visible;

            public Visibility RepairVisibility { get; init; } = Visibility.Collapsed;

            /// <summary>This server currently holds the live connection.</summary>
            public bool IsPreferred { get; init; }

            /// <summary>Label on the single action: give it the connection, or release it.</summary>
            public string ActionText { get; init; } = string.Empty;
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            SetStatus(Str("Cloud247Loading", "Checking what the cloud is watching..."));

            // Pull down anything paired in game while this app was closed, before
            // listing. A server the cloud received but this app has never seen would
            // otherwise show here as covered while being absent from the server list,
            // which is a confusing way to learn the two are the same thing.
            var imported = await CloudPairingImporter.ImportAsync();

            var overview = await CloudSessionsApi.GetOverviewAsync();

            if (overview == null)
            {
                // Not the same as "nothing is covered". Claiming a server is
                // uncovered because a request failed would be worse than admitting
                // we could not find out.
                SetStatus(Str("Cloud247Unavailable", "Could not reach the cloud. Cover is unchanged; this list may be out of date."));
                EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            _plan = overview.Plan;
            ApplyPlan(overview.Plan);

            _rows.Clear();

            foreach (var server in overview.Servers)
                _rows.Add(BuildRow(server, overview.Plan));

            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            SetStatus(imported is { ChangedAnything: true }
                ? string.Format(CultureInfo.CurrentCulture,
                    Str("Cloud247Imported", "Added {0} server(s) and {1} device(s) paired while this app was closed."),
                    imported.Added, imported.DevicesAdded)
                : string.Empty);
        }

        private void ApplyPlan(CloudSessionsApi.CloudPlan plan)
        {
            if (!plan.Access)
            {
                PlanSummaryText.Text = Str("Cloud247NoAccess", "Cloud 24/7 is a supporter feature.");
                PlanHintText.Text = Str("Cloud247NoAccessHint",
                    "Your paired servers are listed below. Supporting the project turns on cover for them.");
                BtnUpgrade.Visibility = Visibility.Visible;
                return;
            }

            BtnUpgrade.Visibility = Visibility.Collapsed;

            PlanSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                Str("Cloud247PlanSummary", "{0} of {1} live connections in use."),
                plan.LiveUsed,
                plan.LiveLimit);

            // The distinction the whole design rests on: cover is unlimited because a
            // watched server holds no connection until there is a reason to, and it
            // sends alarms either way.
            PlanHintText.Text = plan.LiveUsed >= plan.LiveLimit
                ? Str("Cloud247AtLimit",
                    "Every live connection is in use. Other covered servers still send alarms, and one will take a live slot automatically when you play on it.")
                : Str("Cloud247PlanHint",
                    "Covered servers always send alarms. A live connection adds the map, team chat and device control, and moves to whichever server you are playing on.");
        }

        private ServerRow BuildRow(CloudSessionsApi.CloudServer server, CloudSessionsApi.CloudPlan plan)
        {
            var (ownerText, ownerBrush) = DescribeOwner(server);

            var detail = server.NeedsRepair
                ? Str("Cloud247RepairHint", "Re-pair this server in game so the cloud can connect again.")
                : server.LastConnectedAt is { } at
                    ? string.Format(CultureInfo.CurrentCulture,
                        Str("Cloud247LastConnected", "Last connected {0}"), at.ToLocalTime())
                    : server.LastError ?? string.Empty;

            // Said here because there is nowhere else it could be found. A setting
            // pinned on the website stops following this app, so somebody who
            // changes it here and watches the cloud carry on regardless would
            // otherwise have no way to learn why.
            if (server.HasCloudOverrides)
            {
                var pinned = Str("Cloud247Overridden",
                    "Some chat settings for this server are set on the website and no longer follow this app.");

                detail = string.IsNullOrWhiteSpace(detail) ? pinned : detail + "  " + pinned;
            }

            return new ServerRow
            {
                UserServerId = server.UserServerId,
                ServerKey = server.ServerKey,
                Name = string.IsNullOrWhiteSpace(server.Name)
                    ? server.ServerKey ?? Str("Cloud247UnnamedServer", "Unnamed server")
                    : server.Name!,
                Enrolled = server.Enrolled,
                CanToggle = plan.Access,
                OwnerText = ownerText,
                OwnerBrush = ownerBrush,
                DetailText = detail,
                RepairVisibility = server.NeedsRepair ? Visibility.Visible : Visibility.Collapsed,
                IsPreferred = server.IsPreferred,
                ActionText = server.IsPreferred
                    ? Str("Cloud247Release", "Release connection")
                    : Str("Cloud247GiveConnection", "Give live connection"),
            };
        }

        /// <summary>
        /// Who is holding the connection, said plainly.
        ///
        /// A dormant server reads as "alarms only" rather than "offline", because
        /// push cover is real cover — calling it offline would send people to
        /// support over behaviour that is working as designed.
        /// </summary>
        private (string, Brush) DescribeOwner(CloudSessionsApi.CloudServer server)
        {
            // Not "offline" and not "not covered": raid alarms already arrive for
            // every paired server, because the push listener is one socket per
            // account. What this server is missing is the live connection.
            if (!server.IsPreferred && !server.Enrolled)
                return (Str("Cloud247AlarmsOnly", "Raid alarms only"), Brush("TextSubtle"));

            if (server.NeedsRepair)
                return (Str("Cloud247NeedsRepair", "Pairing needs renewing"), Brush("DangerBrush", Brushes.IndianRed));

            if (string.Equals(server.Owner, "desktop", StringComparison.OrdinalIgnoreCase))
                return (Str("Cloud247OwnerDesktop", "This app is connected, the cloud is standing by"), Brush("TextPrimary"));

            if (string.Equals(server.Owner, "cloud", StringComparison.OrdinalIgnoreCase)
                && string.Equals(server.Mode, "live", StringComparison.OrdinalIgnoreCase))
                return (Str("Cloud247OwnerCloud", "The cloud is watching this server"), Brush("Accent"));

            return (Str("Cloud247Dormant", "Alarms only, until you play on it"), Brush("TextSubtle"));
        }

        /// <summary>
        /// The one action: give this server the live connection, or release it.
        ///
        /// There is only one decision to make. Alarms reach every paired server
        /// regardless, so a server enrolled but not chosen had no effect the user
        /// could observe — which made two controls for one intent.
        /// </summary>
        private async void ToggleCover_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ServerRow row }) return;

            SetStatus(Str("Cloud247Saving", "Saving..."));

            var ok = row.IsPreferred
                ? await CloudSessionsApi.DisableAsync(row.UserServerId)
                : await CloudSessionsApi.SetPreferredAsync(row.UserServerId);

            if (!ok)
            {
                SetStatus(Str("Cloud247SaveFailed", "That did not save. Nothing has changed."));
            }

            // Re-read rather than assume: the platform may have refused for a reason
            // this app cannot see, and showing the toggle flipped anyway would be a
            // lie about what is covered.
            await RefreshAsync();
        }

        private void BtnRepair_Click(object sender, RoutedEventArgs e)
        {
            // The one failure only the user can fix, and it needs the game, not us.
            MessageBox.Show(
                Str("Cloud247RepairBody",
                    "Open Rust, go to the server, and pair it again from the in-game menu. The cloud will pick it up automatically once you have."),
                Str("Cloud247Repair", "Re-pair"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private void BtnUpgrade_Click(object sender, RoutedEventArgs e)
            => Open("https://rustplusdesktop.cloud/dashboard/cloud-connect");

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private static void Open(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Opening a browser is a convenience; failing at it is not worth an error.
            }
        }

        private void SetStatus(string text) => StatusText.Text = text;

        /// <summary>A localized string, falling back to English rather than a blank label.</summary>
        private static string Str(string key, string fallback)
            => Application.Current?.TryFindResource(key) as string ?? fallback;

        private static Brush Brush(string key, Brush? fallback = null)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback ?? Brushes.Gray;
    }
}
