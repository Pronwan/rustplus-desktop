using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // ─── ROW VMs ─────────────────────────────────────────────────────────────

    private sealed class GroupMemberRow
    {
        public string BMId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsOnline { get; set; }
        public string StatusText { get; set; } = "offline";
        public Brush StatusBrush { get; set; } = Brushes.Gray;
    }

    // ─── STATE ──────────────────────────────────────────────────────────────

    private HashSet<string> _prevOnlineBMIds = new();
    private bool _tracker_subscribed;
    private string? _tracker_selectedGroupId;
    private bool _tracker_fetchInFlight;

    // ─── INIT (called once from MainWindow ctor) ─────────────────────────────

    private void InitTrackerTab()
    {
        if (_tracker_subscribed) return;
        _tracker_subscribed = true;

        PlayerGroupsService.OnGroupsChanged += () => Dispatcher.Invoke(() =>
        {
            RefreshGroupsList();
        });

        // Fetch online players whenever the Tracker tab becomes the active main tab.
        if (MainTabs != null) MainTabs.SelectionChanged += MainTabs_SelectionChanged_Tracker;
        // Also re-fetch when switching back into the Online sub-tab.
        if (TrackerSubTabs != null) TrackerSubTabs.SelectionChanged += TrackerSubTabs_SelectionChanged;

        RefreshGroupsList();
        RefreshTrackerOnlineList();
    }

    private void MainTabs_SelectionChanged_Tracker(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != MainTabs) return; // ignore bubbled events from inner tabs
        if (MainTabs.SelectedItem == TabTracker) TriggerOnlineFetchIfPossible();
    }

    private void TrackerSubTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != TrackerSubTabs) return;
        // Sub-tab index 0 == Online (XAML order).
        if (TrackerSubTabs.SelectedIndex == 0) TriggerOnlineFetchIfPossible();
    }

    private async void TriggerOnlineFetchIfPossible()
    {
        if (_tracker_fetchInFlight) return;
        if (_vm?.Selected == null || string.IsNullOrEmpty(_vm.Selected.Host)) return;

        _tracker_fetchInFlight = true;
        try
        {
            if (TxtTrackerOnlineStatus != null)
                TxtTrackerOnlineStatus.Text = "Synchronizing with Battlemetrics...";
            if (PnlTrackerOnlineStatus != null) PnlTrackerOnlineStatus.Visibility = Visibility.Visible;
            if (PbTrackerOnlineLoading != null) PbTrackerOnlineLoading.Visibility = Visibility.Visible;

            await TrackingService.FetchOnlinePlayersNowAsync();
            // OnOnlinePlayersUpdated -> RefreshTrackerOnlineList will fire on completion.
        }
        catch (Exception ex)
        {
            if (TxtTrackerOnlineStatus != null) TxtTrackerOnlineStatus.Text = $"Error: {ex.Message}";
            if (PbTrackerOnlineLoading != null) PbTrackerOnlineLoading.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _tracker_fetchInFlight = false;
        }
    }

    // ─── ONLINE SUB-TAB ──────────────────────────────────────────────────────

    private void RefreshTrackerOnlineList()
    {
        if (ListTrackerOnline == null) return;

        var players = TrackingService.LastOnlinePlayers;

        var filterTxt = TxtTrackerOnlineFilter?.Text ?? "";
        if (!string.IsNullOrEmpty(filterTxt) && filterTxt != "Filter players...")
        {
            players = players
                .Where(p => p.Name.Contains(filterTxt, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        ListTrackerOnline.ItemsSource = null;
        ListTrackerOnline.ItemsSource = players;

        if (TrackingService.LastOnlinePlayers.Count == 0)
        {
            bool serverSelected = _vm?.Selected != null && !string.IsNullOrEmpty(_vm.Selected.Host);
            string msg = !string.IsNullOrEmpty(TrackingService.StatusMessage)
                ? TrackingService.StatusMessage
                : serverSelected
                    ? "Loading online players..."
                    : "Connect to a server to load online players";
            if (TxtTrackerOnlineStatus != null) TxtTrackerOnlineStatus.Text = msg;
            if (PnlTrackerOnlineStatus != null) PnlTrackerOnlineStatus.Visibility = Visibility.Visible;
            if (PbTrackerOnlineLoading != null)
                PbTrackerOnlineLoading.Visibility = serverSelected ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            if (PnlTrackerOnlineStatus != null) PnlTrackerOnlineStatus.Visibility = Visibility.Collapsed;
        }
    }

    private void TxtTrackerOnlineFilter_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtTrackerOnlineFilter.Text == "Filter players...")
        {
            TxtTrackerOnlineFilter.Text = "";
            TxtTrackerOnlineFilter.Foreground = Brushes.White;
        }
    }

    private void TxtTrackerOnlineFilter_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtTrackerOnlineFilter.Text))
        {
            TxtTrackerOnlineFilter.Text = "Filter players...";
            TxtTrackerOnlineFilter.Foreground = Brushes.Gray;
        }
    }

    private void TxtTrackerOnlineFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtTrackerOnlineFilter.Text != "Filter players...") RefreshTrackerOnlineList();
    }

    // ─── GROUP ASSIGNMENT (from Online sub-tab) ──────────────────────────────

    private void BtnAssignGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not OnlinePlayerBM player) return;

        var menu = new ContextMenu();
        if (TryFindResource("DarkContextMenu") is Style ctxStyle) menu.Style = ctxStyle;
        var darkMenuItem = TryFindResource("DarkMenuItem") as Style;
        var darkSeparator = TryFindResource("DarkSeparator") as Style;

        var groups = PlayerGroupsService.Groups;
        var current = PlayerGroupsService.GetGroupForPlayer(player.BMId);

        MenuItem MakeItem(string header)
        {
            var mi = new MenuItem { Header = header };
            if (darkMenuItem != null) mi.Style = darkMenuItem;
            return mi;
        }

        Separator MakeSep()
        {
            var s = new Separator();
            if (darkSeparator != null) s.Style = darkSeparator;
            return s;
        }

        if (groups.Count == 0)
        {
            var hint = MakeItem("(no groups yet)");
            hint.IsEnabled = false;
            menu.Items.Add(hint);
            menu.Items.Add(MakeSep());
        }

        foreach (var g in groups)
        {
            var item = MakeItem(g.Name);
            item.IsCheckable = true;
            item.IsChecked = current?.Id == g.Id;
            item.Icon = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(2),
                Background = BrushFromHex(g.ColorHex)
            };
            string capturedId = g.Id;
            item.Click += (_, __) =>
            {
                PlayerGroupsService.AssignPlayerToGroup(player.BMId, capturedId);
                EnsurePlayerTracked(player);
                RefreshTrackerOnlineList();
            };
            menu.Items.Add(item);
        }

        if (current != null)
        {
            menu.Items.Add(MakeSep());
            var removeItem = MakeItem("Remove from group");
            removeItem.Click += (_, __) =>
            {
                PlayerGroupsService.RemovePlayerFromGroup(player.BMId);
                RefreshTrackerOnlineList();
            };
            menu.Items.Add(removeItem);
        }

        menu.Items.Add(MakeSep());
        var newItem = MakeItem("+ New group...");
        newItem.Click += (_, __) =>
        {
            var name = PromptForString("New group", "Group name:");
            if (string.IsNullOrWhiteSpace(name)) return;
            var newGroup = PlayerGroupsService.CreateGroup(name!);
            PlayerGroupsService.AssignPlayerToGroup(player.BMId, newGroup.Id);
            EnsurePlayerTracked(player);
            RefreshTrackerOnlineList();
        };
        menu.Items.Add(newItem);

        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void EnsurePlayerTracked(OnlinePlayerBM player)
    {
        if (!player.IsTracked)
        {
            TrackingService.TrackPlayer(player.BMId, player.Name, _vm.Selected?.Name ?? "Unknown");
            player.IsTracked = true;
        }
    }

    // ─── GROUPS SUB-TAB ──────────────────────────────────────────────────────

    private void RefreshGroupsList()
    {
        if (CmbGroupSelector == null) return;

        var groups = PlayerGroupsService.Groups;
        var prevSelectedId = _tracker_selectedGroupId;

        CmbGroupSelector.ItemsSource = null;
        CmbGroupSelector.ItemsSource = groups;

        if (groups.Count == 0)
        {
            _tracker_selectedGroupId = null;
            if (PnlGroupHeader != null) PnlGroupHeader.Visibility = Visibility.Collapsed;
            if (TxtGroupOnlineCount != null) TxtGroupOnlineCount.Visibility = Visibility.Collapsed;
            if (ListGroupMembers != null)
            {
                ListGroupMembers.Visibility = Visibility.Collapsed;
                ListGroupMembers.ItemsSource = null;
            }
            if (TxtNoGroup != null) TxtNoGroup.Visibility = Visibility.Visible;
            return;
        }

        if (TxtNoGroup != null) TxtNoGroup.Visibility = Visibility.Collapsed;

        var toSelect = groups.FirstOrDefault(g => g.Id == prevSelectedId) ?? groups[0];
        CmbGroupSelector.SelectedItem = toSelect;
    }

    private void CmbGroupSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var group = CmbGroupSelector.SelectedItem as PlayerGroup;
        _tracker_selectedGroupId = group?.Id;
        RefreshSelectedGroupDetail();
    }

    private void RefreshSelectedGroupDetail()
    {
        var group = CmbGroupSelector?.SelectedItem as PlayerGroup;

        if (group == null)
        {
            if (PnlGroupHeader != null) PnlGroupHeader.Visibility = Visibility.Collapsed;
            if (TxtGroupOnlineCount != null) TxtGroupOnlineCount.Visibility = Visibility.Collapsed;
            if (ListGroupMembers != null) ListGroupMembers.Visibility = Visibility.Collapsed;
            return;
        }

        if (PnlGroupHeader != null) PnlGroupHeader.Visibility = Visibility.Visible;
        if (ListGroupMembers != null) ListGroupMembers.Visibility = Visibility.Visible;

        if (TxtSelectedGroupName != null) TxtSelectedGroupName.Text = group.Name;
        if (GroupColorSwatch != null) GroupColorSwatch.Background = BrushFromHex(group.ColorHex);
        if (ChkGroupNotify != null) ChkGroupNotify.IsChecked = group.NotifyOnOnline;

        var trackedByBMId = TrackingService.GetTrackedPlayers().ToDictionary(p => p.BMId, p => p);
        var onlineByBMId = TrackingService.LastOnlinePlayers.ToDictionary(p => p.BMId, p => p);

        var rows = group.BMIds.Select(bmId =>
        {
            string name = onlineByBMId.TryGetValue(bmId, out var o) ? o.Name
                : trackedByBMId.TryGetValue(bmId, out var t) ? t.Name
                : "(unknown)";
            bool isOnline = onlineByBMId.ContainsKey(bmId);
            string statusText = isOnline ? $"online · {onlineByBMId[bmId].PlayTimeStr}" : "offline";
            return new GroupMemberRow
            {
                BMId = bmId,
                Name = name,
                IsOnline = isOnline,
                StatusText = statusText,
                StatusBrush = isOnline ? Brushes.LimeGreen : new SolidColorBrush(Color.FromRgb(0x55, 0x5a, 0x60))
            };
        })
        .OrderByDescending(r => r.IsOnline)
        .ThenBy(r => r.Name)
        .ToList();

        if (ListGroupMembers != null)
        {
            ListGroupMembers.ItemsSource = null;
            ListGroupMembers.ItemsSource = rows;
        }

        if (TxtGroupOnlineCount != null)
        {
            int onlineCount = rows.Count(r => r.IsOnline);
            TxtGroupOnlineCount.Text = $"{onlineCount}/{rows.Count} online";
            TxtGroupOnlineCount.Visibility = Visibility.Visible;
        }
    }

    private void BtnNewGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForString("New group", "Group name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        var g = PlayerGroupsService.CreateGroup(name!);
        _tracker_selectedGroupId = g.Id;
        // RefreshGroupsList runs via OnGroupsChanged event
    }

    private void BtnGroupRename_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGroupSelector?.SelectedItem is not PlayerGroup g) return;
        var name = PromptForString("Rename group", "New name:", g.Name);
        if (string.IsNullOrWhiteSpace(name) || name == g.Name) return;
        PlayerGroupsService.RenameGroup(g.Id, name!);
    }

    private void BtnGroupDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGroupSelector?.SelectedItem is not PlayerGroup g) return;
        var result = MessageBox.Show(
            $"Delete group \"{g.Name}\"?\n\nMembers will not be untracked, just ungrouped.",
            "Delete group", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;
        PlayerGroupsService.DeleteGroup(g.Id);
        _tracker_selectedGroupId = null;
    }

    private void BtnGroupColor_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGroupSelector?.SelectedItem is not PlayerGroup g) return;
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = ColorFromHex(g.ColorHex)
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            PlayerGroupsService.SetGroupColor(g.Id, hex);
        }
    }

    private void ChkGroupNotify_Click(object sender, RoutedEventArgs e)
    {
        if (CmbGroupSelector?.SelectedItem is not PlayerGroup g) return;
        bool enabled = ChkGroupNotify.IsChecked == true;
        PlayerGroupsService.SetGroupNotify(g.Id, enabled);
    }

    private void BtnGroupMemberBM_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not GroupMemberRow row) return;
        if (string.IsNullOrEmpty(row.BMId)) return;
        try
        {
            Process.Start(new ProcessStartInfo($"https://www.battlemetrics.com/players/{row.BMId}")
            { UseShellExecute = true });
        }
        catch { /* swallow */ }
    }

    private void BtnRemoveFromGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not GroupMemberRow row) return;
        PlayerGroupsService.RemovePlayerFromGroup(row.BMId);
        // RefreshGroupsList runs via OnGroupsChanged
    }

    // ─── CAME-ONLINE NOTIFICATIONS ───────────────────────────────────────────

    /// <summary>
    /// Called from OnOnlinePlayersUpdated. Diff against the previous snapshot,
    /// fire toast for any newly-online member of a group with NotifyOnOnline=true.
    /// </summary>
    private void NotifyGroupCameOnlineIfAny()
    {
        var current = TrackingService.LastOnlinePlayers;
        var currentBMIds = current.Select(p => p.BMId).ToHashSet();

        // Skip the very first call (no baseline yet) so we don't toast everyone who's already online.
        if (_prevOnlineBMIds.Count == 0)
        {
            _prevOnlineBMIds = currentBMIds;
            return;
        }

        var newlyOnline = current.Where(p => !_prevOnlineBMIds.Contains(p.BMId)).ToList();
        _prevOnlineBMIds = currentBMIds;
        if (newlyOnline.Count == 0) return;

        // Bucket by group
        var byGroup = new Dictionary<string, (PlayerGroup g, List<string> names)>();
        foreach (var p in newlyOnline)
        {
            var g = PlayerGroupsService.GetGroupForPlayer(p.BMId);
            if (g == null || !g.NotifyOnOnline) continue;
            if (!byGroup.TryGetValue(g.Id, out var entry))
            {
                entry = (g, new List<string>());
                byGroup[g.Id] = entry;
            }
            entry.names.Add(p.Name);
        }

        foreach (var (_, entry) in byGroup)
        {
            string title = $"[{entry.g.Name}] online";
            string body = string.Join(", ", entry.names.Take(5))
                          + (entry.names.Count > 5 ? $" +{entry.names.Count - 5} more" : "");
            App.ShowTrayToast(title, body);
        }
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────────

    private static SolidColorBrush BrushFromHex(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x35));
        }
    }

    private static System.Drawing.Color ColorFromHex(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        }
        catch
        {
            return System.Drawing.Color.FromArgb(0xFF, 0x6B, 0x35);
        }
    }

    /// <summary>Modal text-input dialog. Returns null if cancelled.</summary>
    private string? PromptForString(string title, string label, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 320,
            Height = 150,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("AppBg"),
            Foreground = (Brush)FindResource("TextPrimary")
        };

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var txt = new TextBox
        {
            Text = initial,
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1c, 0x1e)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
        };
        Grid.SetRow(txt, 1);
        grid.Children.Add(txt);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 70, IsCancel = true };
        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        Grid.SetRow(btnPanel, 3);
        grid.Children.Add(btnPanel);

        win.Content = grid;

        string? result = null;
        ok.Click += (_, __) => { result = txt.Text; win.Close(); };
        cancel.Click += (_, __) => { result = null; win.Close(); };

        win.Loaded += (_, __) => { txt.Focus(); txt.SelectAll(); };
        win.ShowDialog();
        return result;
    }
}
