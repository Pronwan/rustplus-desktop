using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private DispatcherTimer? _teamSyncPullTimer;
    private bool _teamSyncWired;
    private bool _suppressNotifyDuringMerge;

    /// <summary>Idempotent. Called once during MainWindow construction.</summary>
    private void InitTeamSync()
    {
        if (_teamSyncWired) return;
        _teamSyncWired = true;

        // Context delegates so TeamSyncService can read live state without depending on MainWindow.
        TeamSyncService.GetMySteamId = () => _mySteamId;
        TeamSyncService.GetServerKey = () =>
        {
            var prof = _vm?.Selected;
            if (prof == null || string.IsNullOrEmpty(prof.Host)) return "";
            return $"{prof.Host}-{prof.Port}";
        };
        TeamSyncService.GetTeammateSteamIds = () =>
        {
            // ObservableCollection<TeamMemberVM> with `SteamId` (ulong)
            var list = new List<ulong>();
            foreach (var tm in TeamMembers) if (tm.SteamId != 0) list.Add(tm.SteamId);
            return list;
        };

        // Mutation → upload (coalesced).
        TrackingService.OnTrackedDirectoryChanged += () =>
        {
            if (_suppressNotifyDuringMerge) return;
            TeamSyncService.NotifyLocalChanged();
        };
        PlayerGroupsService.OnGroupsChanged += () =>
        {
            if (_suppressNotifyDuringMerge) return;
            TeamSyncService.NotifyLocalChanged();
        };

        // Status updates → reflect into the small status line above sub-tabs.
        TeamSyncService.OnSyncStatus += s => Dispatcher.Invoke(() => UpdateTeamSyncStatusText(s));

        // 30-second pull timer.
        _teamSyncPullTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _teamSyncPullTimer.Tick += async (_, __) => await PullTeamSyncSafelyAsync();
        _teamSyncPullTimer.Start();
    }

    private async System.Threading.Tasks.Task PullTeamSyncSafelyAsync()
    {
        if (!TrackingService.TeamSyncEnabled) return;
        try
        {
            _suppressNotifyDuringMerge = true;
            await TeamSyncService.PullAndMergeAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[team-sync] pull failed: {ex.Message}");
        }
        finally
        {
            _suppressNotifyDuringMerge = false;
        }
    }

    private void UpdateTeamSyncStatusText(string statusFromService)
    {
        if (TxtTeamSyncStatus == null) return;
        var pull = TeamSyncService.LastPullAt;
        var pullStr = pull.HasValue ? $"sync {pull.Value.ToLocalTime():HH:mm:ss}" : "";
        TxtTeamSyncStatus.Text = string.IsNullOrEmpty(statusFromService)
            ? pullStr
            : $"{pullStr} · {statusFromService}";
    }

    // ─── Manual sync button (rendered from XAML) ─────────────────────────────
    // The single right-side refresh in the Tracker top bar now drives both
    // BattleMetrics online-players refresh AND team-sync upload+pull, so the
    // user gets one button that "refreshes everything". Team sync is skipped
    // silently when disabled in Settings.
    private async void BtnTeamSyncNow_Click(object sender, RoutedEventArgs e)
    {
        if (BtnTeamSyncNow != null) BtnTeamSyncNow.IsEnabled = false;
        try
        {
            BtnTrackerRefresh_Click(sender, e);

            if (TrackingService.TeamSyncEnabled)
                await TeamSyncService.ForceSyncAsync();
        }
        finally
        {
            if (BtnTeamSyncNow != null) BtnTeamSyncNow.IsEnabled = true;
        }
    }

    // ─── Settings toggle handler (rendered from XAML) ────────────────────────
    private void ChkTeamSyncEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            TrackingService.TeamSyncEnabled = cb.IsChecked == true;
            UpdateTeamSyncStatusText(TrackingService.TeamSyncEnabled ? "enabled" : "disabled");
        }
    }
}
