using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Services;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// Cloud 24/7: Global consent and active live server configuration.
    /// Reads and writes the same contract as the web dashboard.
    /// Decoupled from smart home / Alexa settings.
    /// </summary>
    public partial class Cloud24x7Window : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<ServerRow> _rows = new();
        private readonly ObservableCollection<ServerOption> _serverOptions = new();
        private CloudSessionsApi.CloudPlan? _plan;
        private bool _isUpdatingUi;

        public Cloud24x7Window()
        {
            InitializeComponent();
            ServerList.ItemsSource = _rows;
            CmbActiveServer.ItemsSource = _serverOptions;
            Loaded += async (_, _) => await RefreshAsync();
        }

        public sealed class ServerOption
        {
            public string ServerId { get; init; } = string.Empty;
            public string UserServerId { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        /// <summary>One server as the list shows it.</summary>
        public sealed class ServerRow
        {
            public string UserServerId { get; init; } = string.Empty;
            public string ServerId { get; init; } = string.Empty;
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

            public Visibility ActiveBadgeVisibility
                => (IsPreferred && CloudSessionsApi.GlobalConsentEnabled) ? Visibility.Visible : Visibility.Collapsed;

            public Wpf.Ui.Controls.ControlAppearance ActionAppearance
                => IsPreferred ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;

            /// <summary>Label on the single action: activate, or release.</summary>
            public string ActionText { get; init; } = string.Empty;
        }

        private async System.Threading.Tasks.Task RefreshAsync()
        {
            SetStatus(Str("Cloud247Loading", "Checking what the cloud is watching..."));

            var imported = await CloudPairingImporter.ImportAsync();
            var overview = await CloudSessionsApi.GetOverviewAsync();

            if (overview == null)
            {
                SetStatus(Str("Cloud247Unavailable", "Could not reach the cloud. Cover is unchanged; this list may be out of date."));
                EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            _plan = overview.Plan;

            _isUpdatingUi = true;
            try
            {
                ApplyPlan(overview.Plan);

                _rows.Clear();
                _serverOptions.Clear();

                foreach (var server in overview.Servers)
                {
                    _rows.Add(BuildRow(server, overview.Plan));
                    _serverOptions.Add(new ServerOption
                    {
                        ServerId = server.ServerId,
                        UserServerId = server.UserServerId,
                        Name = string.IsNullOrWhiteSpace(server.Name) ? (server.ServerKey ?? "Server") : server.Name!
                    });
                }

                if (overview.Plan.LiveLimit <= 1 && !string.IsNullOrWhiteSpace(overview.Plan.PreferredServerId))
                {
                    CmbActiveServer.SelectedValue = overview.Plan.PreferredServerId;
                }
                else if (_serverOptions.Count > 0 && CmbActiveServer.SelectedIndex < 0)
                {
                    var firstPreferred = overview.Servers.FirstOrDefault(s => s.IsPreferred);
                    if (firstPreferred != null)
                    {
                        CmbActiveServer.SelectedValue = firstPreferred.ServerId;
                    }
                }

                EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                SetStatus(imported is { ChangedAnything: true }
                    ? string.Format(CultureInfo.CurrentCulture,
                        Str("Cloud247Imported", "Added {0} server(s) and {1} device(s) paired while this app was closed."),
                        imported.Added, imported.DevicesAdded)
                    : string.Empty);
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void ApplyPlan(CloudSessionsApi.CloudPlan plan)
        {
            ToggleGlobalConsent.IsChecked = plan.GlobalConsent;

            if (!plan.Access)
            {
                PlanSummaryText.Text = Str("Cloud247NoAccess", "Cloud 24/7 is a supporter feature.");
                PlanHintText.Text = Str("Cloud247NoAccessHint",
                    "Your paired servers are listed below. Supporting the project turns on cover for them.");
                BtnUpgrade.Visibility = Visibility.Visible;
                ToggleGlobalConsent.IsEnabled = false;
                SingleServerDropdownPanel.Visibility = Visibility.Collapsed;
                return;
            }

            BtnUpgrade.Visibility = Visibility.Collapsed;
            ToggleGlobalConsent.IsEnabled = true;

            int activeCount = plan.ActiveServerIds?.Count ?? (string.IsNullOrWhiteSpace(plan.PreferredServerId) ? 0 : 1);

            PlanSummaryText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} of {1} active server allowance selected.",
                activeCount,
                plan.LiveLimit);

            PlanHintText.Text = plan.GlobalConsent
                ? (plan.LiveLimit > 1
                    ? $"Multi-server plan: You can choose up to {plan.LiveLimit} active servers simultaneously for 24/7 coverage."
                    : "Single active server plan: Choose which 1 server holds the live 24/7 connection.")
                : "Cloud 24/7 is currently disabled globally. No cloud background sessions are running.";

            SingleServerDropdownPanel.Visibility = (plan.GlobalConsent && plan.LiveLimit <= 1)
                ? Visibility.Visible
                : Visibility.Collapsed;
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

            if (server.HasCloudOverrides)
            {
                var pinned = Str("Cloud247Overridden",
                    "Some chat settings for this server are set on the website and no longer follow this app.");

                detail = string.IsNullOrWhiteSpace(detail) ? pinned : detail + "  " + pinned;
            }

            string actionText;
            if (!plan.GlobalConsent)
            {
                actionText = "Turn on 24/7";
            }
            else if (server.IsPreferred)
            {
                actionText = "Deactivate";
            }
            else
            {
                actionText = "Set Active";
            }

            return new ServerRow
            {
                UserServerId = server.UserServerId,
                ServerId = server.ServerId,
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
                ActionText = actionText,
            };
        }

        private (string, Brush) DescribeOwner(CloudSessionsApi.CloudServer server)
        {
            if (!CloudSessionsApi.GlobalConsentEnabled)
                return ("Cloud 24/7 Disabled", Brush("TextSubtle"));

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

        private bool PromptConsentConfirmationIfNeeded()
        {
            if (_plan?.GlobalConsent == true)
            {
                return true;
            }

            var dialog = new Dialogs.Cloud247ConsentWindow(this);
            return dialog.ShowDialog() == true && dialog.Accepted;
        }

        private async void ToggleGlobalConsent_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingUi) return;

            bool enabling = ToggleGlobalConsent.IsChecked == true;
            if (enabling)
            {
                if (!PromptConsentConfirmationIfNeeded())
                {
                    _isUpdatingUi = true;
                    ToggleGlobalConsent.IsChecked = false;
                    _isUpdatingUi = false;
                    return;
                }
            }

            SetStatus("Updating Cloud 24/7 settings...");

            var currentActive = _plan?.ActiveServerIds?.ToList() ?? new List<string>();
            if (enabling && currentActive.Count == 0)
            {
                var first = _rows.FirstOrDefault();
                if (first != null) currentActive.Add(first.ServerId);
            }
            else if (!enabling)
            {
                currentActive.Clear();
            }

            var ok = await CloudSessionsApi.UpdateGlobalSettingsAsync(enabling, currentActive);
            if (!ok)
            {
                SetStatus("Failed to update Cloud 24/7 settings.");
            }
            else
            {
                _ = CloudConsentService.RecordConsentAsync(CloudConsentService.TypeCloud247, enabling);
                if (enabling)
                {
                    _ = FcmSyncService.SyncFcmCredentialsAsync();
                }
            }

            await RefreshAsync();
        }

        private async void CmbActiveServer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            if (CmbActiveServer.SelectedValue is not string selectedId || string.IsNullOrWhiteSpace(selectedId)) return;

            if (_plan?.GlobalConsent != true)
            {
                if (!PromptConsentConfirmationIfNeeded())
                {
                    await RefreshAsync();
                    return;
                }
            }

            SetStatus("Switching active Cloud 24/7 server...");
            var ok = await CloudSessionsApi.UpdateGlobalSettingsAsync(true, new[] { selectedId });
            if (!ok)
            {
                SetStatus("Failed to switch active server.");
            }
            else
            {
                _ = FcmSyncService.SyncFcmCredentialsAsync();
            }

            await RefreshAsync();
        }

        private async void ToggleCover_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ServerRow row }) return;

            var currentActive = _plan?.ActiveServerIds?.ToList() ?? new List<string>();
            int limit = _plan?.LiveLimit ?? 1;

            if (!row.IsPreferred)
            {
                if (_plan?.GlobalConsent != true)
                {
                    if (!PromptConsentConfirmationIfNeeded())
                    {
                        return;
                    }
                }

                if (limit <= 1)
                {
                    currentActive.Clear();
                    currentActive.Add(row.ServerId);
                }
                else
                {
                    while (currentActive.Count >= limit && currentActive.Count > 0)
                    {
                        currentActive.RemoveAt(0);
                    }
                    currentActive.Add(row.ServerId);
                }
            }
            else
            {
                // Deactivate: Remove from active list
                currentActive.RemoveAll(id => 
                    string.Equals(id, row.ServerId, StringComparison.OrdinalIgnoreCase) || 
                    string.Equals(id, row.UserServerId, StringComparison.OrdinalIgnoreCase));
            }

            SetStatus("Saving...");
            bool enableGlobal = currentActive.Count > 0;
            var ok = await CloudSessionsApi.UpdateGlobalSettingsAsync(enableGlobal, currentActive);

            if (!ok)
            {
                SetStatus("That did not save. Nothing has changed.");
            }
            else
            {
                _ = CloudConsentService.RecordConsentAsync(CloudConsentService.TypeCloud247, enableGlobal);
                if (enableGlobal)
                {
                    _ = FcmSyncService.SyncFcmCredentialsAsync();
                }
            }

            await RefreshAsync();
        }

        private void BtnRepair_Click(object sender, RoutedEventArgs e)
        {
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
            catch { }
        }

        private void SetStatus(string text) => StatusText.Text = text;

        private static string Str(string key, string fallback)
            => Application.Current?.TryFindResource(key) as string ?? fallback;

        private static Brush Brush(string key, Brush? fallback = null)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback ?? Brushes.Gray;
    }
}
