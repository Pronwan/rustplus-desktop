using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using ui = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private void BtnViewTracked_Click(object sender, RoutedEventArgs e)
    {
        var bmId = (sender as FrameworkElement)?.Tag as string 
                   ?? ((sender as FrameworkElement)?.DataContext as TrackedPlayer)?.BMId;
        if (!string.IsNullOrEmpty(bmId)) ShowTrackingAnalysisWindow(bmId);
    }

    private void BtnGroupTracked_Click(object sender, RoutedEventArgs e)
    {
        var player = (sender as FrameworkElement)?.DataContext as TrackedPlayer;
        if (player == null) return;
        var result = ShowGroupEditorDialog(player);
        if (result != null) {
            TrackingService.SetPlayerGroup(player.BMId, result.Value.name, result.Value.color);
            RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
        }
    }

    private void BtnRenameTracked_Click(object sender, RoutedEventArgs e)
    {
        var player = (sender as FrameworkElement)?.DataContext as TrackedPlayer;
        if (player == null) return;
        var newName = ShowInputBox($"Enter new name for {player.BMId}:", "Rename Player", player.Name);
        if (!string.IsNullOrWhiteSpace(newName)) {
            TrackingService.RenameTrackedPlayer(player.BMId, newName);
            RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
        }
    }

    private void BtnRemoveTracked_Click(object sender, RoutedEventArgs e)
    {
        var bmId = ((sender as FrameworkElement)?.DataContext as TrackedPlayer)?.BMId;
        if (!string.IsNullOrEmpty(bmId)) {
            TrackingService.UntrackPlayer(bmId);
            RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
        }
    }

    private void BtnMore_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as FrameworkElement;
        if (btn == null) return;
        
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(btn);
        while (parent != null && !(parent is Grid g && g.Name == "PlayerRow"))
        {
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        
        if (parent is Grid grid && grid.ContextMenu != null)
        {
            grid.ContextMenu.PlacementTarget = btn;
            grid.ContextMenu.IsOpen = true;
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
    private void OnOnlinePlayersUpdated()
    {
        Dispatcher.Invoke(() =>
        {
            RefreshOnlinePlayersList();
            RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
            // Update tracking status indicator
            bool anyTracked = TrackingService.GetTrackedPlayers().Count > 0;
            _vm.IsTrackingActive = TrackingService.IsTracking;

            if (!anyTracked)
            {
                TxtTrackingStatus.Text = "Add players to tracker to start tracking";
                TxtTrackingStatus.Foreground = Brushes.Gray;
                TxtTrackingStatus.FontStyle = FontStyles.Italic;
            }
            else
            {
                TxtTrackingStatus.Text = TrackingService.IsTracking ? "Tracking Active" : "Tracking Idle";
                TxtTrackingStatus.Foreground = TrackingService.IsTracking ? Brushes.White : Brushes.Gray;
                TxtTrackingStatus.FontStyle = FontStyles.Normal;
            }
            
            if (TrackingService.LastPullTime.HasValue && anyTracked)
            {
                TxtLastPull.Text = $"Last pull: {TrackingService.LastPullTime.Value:HH:mm:ss}";
            }
            else
            {
                TxtLastPull.Text = "Last pull: --:--";
            }
        });
    }

    private void OnServerInfoUpdated(string description)
    {
        Dispatcher.Invoke(() => {
            if (_vm.Selected != null && string.IsNullOrWhiteSpace(_vm.Selected.Description))
            {
                _vm.Selected.Description = description;
            }
        });
    }

    private void RefreshOnlinePlayersList()
    {
        var players = TrackingService.LastOnlinePlayers;
        
        // Dynamic Filter visibility
        if (players.Count > 0) {
            TxtOnlineFilter.Visibility = Visibility.Visible;
            if (string.IsNullOrEmpty(TxtOnlineFilter.Text) && !TxtOnlineFilter.IsFocused) {
                TxtOnlineFilter.Text = "Filter players...";
                TxtOnlineFilter.Foreground = Brushes.Gray;
            }
        } else {
            TxtOnlineFilter.Visibility = Visibility.Collapsed;
        }

        var filterTxt = TxtOnlineFilter.Text;
        if (!string.IsNullOrEmpty(filterTxt) && filterTxt != "Filter players...")
        {
            players = players.Where(p => p.Name.Contains(filterTxt, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        ListOnlinePlayers.ItemsSource = null;
        ListOnlinePlayers.ItemsSource = players;

        if (TrackingService.LastOnlinePlayers.Count == 0)
        {
            TxtOnlinePlayersStatus.Text = TrackingService.StatusMessage;
            PnlOnlineStatus.Visibility = Visibility.Visible;
            PbOnlineLoading.Visibility = string.IsNullOrEmpty(TrackingService.StatusMessage) || TrackingService.StatusMessage.Contains("Fetching") || TrackingService.StatusMessage.Contains("Looking") ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            PnlOnlineStatus.Visibility = Visibility.Collapsed;
        }

        // Update Server BM button visibility
        var bmVisibility = string.IsNullOrEmpty(TrackingService.CurrentServerBMId) 
            ? Visibility.Collapsed 
            : Visibility.Visible;
        BtnServerBMHeader.Visibility = bmVisibility;
        BtnServerBMPlayers.Visibility = bmVisibility;
    }

    private void TxtOnlineFilter_GotFocus(object sender, RoutedEventArgs e) {
        if (TxtOnlineFilter.Text == "Filter players...") {
            TxtOnlineFilter.Text = "";
            TxtOnlineFilter.Foreground = Brushes.White;
        }
    }
    private void TxtOnlineFilter_LostFocus(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(TxtOnlineFilter.Text)) {
            TxtOnlineFilter.Text = "Filter players...";
            TxtOnlineFilter.Foreground = Brushes.Gray;
        }
    }
    private void TxtOnlineFilter_TextChanged(object sender, TextChangedEventArgs e) {
        if (TxtOnlineFilter.Text != "Filter players...") RefreshOnlinePlayersList();
    }

    private async void BtnShowOnline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Name == "BtnShowOnline")
        {
            MainTabs.SelectedIndex = 3; // Players tab
        }

        if (_vm.Selected == null || string.IsNullOrEmpty(_vm.Selected.Host))
        {
            TxtOnlinePlayersStatus.Text = "Connect to a server to load players list";
            PnlOnlineStatus.Visibility = Visibility.Visible;
            PbOnlineLoading.Visibility = Visibility.Collapsed;
            ListOnlinePlayers.ItemsSource = null;
            return;
        }

        TxtOnlinePlayersStatus.Text = "Synchronizing with Battlemetrics...";
        PnlOnlineStatus.Visibility = Visibility.Visible;
        PbOnlineLoading.Visibility = Visibility.Visible;
        ListOnlinePlayers.ItemsSource = null;

        try
        {
            await TrackingService.FetchOnlinePlayersNowAsync();
        }
        catch (Exception ex)
        {
            TxtOnlinePlayersStatus.Text = $"Error: {ex.Message}";
            PnlOnlineStatus.Visibility = Visibility.Visible;
            PbOnlineLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnTrackPlayer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not OnlinePlayerBM player) return;

        if (player.IsTracked)
        {
            // Already tracked → open inline analysis dialog
            ShowPlayerAnalysis(player.BMId, player.Name);
        }
        else
        {
            TrackingService.TrackPlayer(player.BMId, player.Name, _vm.Selected?.Name ?? "Unknown");
            player.IsTracked = true;
            // Refresh list so button text updates
            RefreshOnlinePlayersList();
            AppendLog($"[tracking] Now tracking {player.Name} from {_vm.Selected?.Name ?? "this server"}");
        }
    }

    private void BtnViewAllAnalysis_Click(object sender, RoutedEventArgs e)
    {
        ShowTrackingAnalysisWindow();
    }

    private void BtnServerBM_Click(object sender, RoutedEventArgs e)
    {
        var serverId = TrackingService.CurrentServerBMId;
        if (!string.IsNullOrEmpty(serverId))
        {
            OpenUrl("https://www.battlemetrics.com/servers/rust/" + serverId);
        }
    }

    private void BtnPlayerBM_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is OnlinePlayerBM player)
        {
            OpenUrl("https://www.battlemetrics.com/players/" + player.BMId);
        }
    }

    private void TxtManualBMId_GotFocus(object sender, RoutedEventArgs e) {
        if (TxtManualBMId.Text == "Manual BM ID...") {
            TxtManualBMId.Text = "";
            TxtManualBMId.Foreground = Brushes.White;
        }
    }
    private void TxtManualBMId_LostFocus(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(TxtManualBMId.Text)) {
            TxtManualBMId.Text = "Manual BM ID...";
            TxtManualBMId.Foreground = Brushes.Gray;
        }
    }

    private async void BtnAddManual_Click(object sender, RoutedEventArgs e)
    {
        var bmId = TxtManualBMId.Text?.Trim();
        if (string.IsNullOrEmpty(bmId) || bmId == "Manual BM ID..." || !bmId.All(char.IsDigit)) return;

        TxtManualBMId.IsEnabled = false;
        BtnAddManual.Content = "...";
        
        var name = await TrackingService.FetchPlayerNameAsync(bmId);
        var lastSession = await TrackingService.FetchPlayerLastSessionAsync(bmId);
        
        var serverName = TrackingService.LastServer.name;
        if (string.IsNullOrEmpty(serverName)) serverName = _vm.Selected?.Name ?? "Manual Add";

        TrackingService.TrackPlayer(bmId, name, serverName, lastSession);
        
        TxtManualBMId.Text = "Manual BM ID...";
        TxtManualBMId.Foreground = Brushes.Gray;
        TxtManualBMId.IsEnabled = true;
        BtnAddManual.Content = "Track ID";
        
        var sessionMsg = lastSession != null ? $" (found last session: {lastSession.ConnectTime.ToLocalTime():g})" : "";
        AppendLog($"[tracking] Manually added {name} ({bmId}) to tracking list on server: {serverName}{sessionMsg}");
        RefreshOnlinePlayersList();
    }

    private void RefreshTrackedPlayersList(string filter = "")
    {
        try
        {
            if (ListTrackedPlayers == null) return;
            ListTrackedPlayers.Children.Clear();
            var players = TrackingService.GetTrackedPlayers();
            if (!string.IsNullOrEmpty(filter) && filter != "Filter players...")
            {
                players = players.Where(p =>
                    (p.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.LastServerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                ).ToList();
            }

            var template = TryFindResource("TrackedPlayerTemplate") as DataTemplate;
            if (template == null) return;

            var serversGrouped = players.GroupBy(p => string.IsNullOrEmpty(p.LastServerName) ? "Global / Legacy" : p.LastServerName);
            foreach (var serverGrp in serversGrouped)
            {
                ListTrackedPlayers.Children.Add(new TextBlock
                {
                    Text = serverGrp.Key,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                    Margin = new Thickness(0, 15, 0, 5),
                    FontSize = 14,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = serverGrp.Key
                });

                var subgroups = serverGrp.GroupBy(p => string.IsNullOrEmpty(p.GroupName) ? "Ungrouped" : p.GroupName)
                    .OrderBy(g => g.Key == "Ungrouped" ? 1 : 0)
                    .ThenBy(g => g.Key);

                foreach (var group in subgroups)
                {
                    var groupHeaderPanel = new Grid { Margin = new Thickness(5, 5, 0, 5) };
                    groupHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    groupHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var headerColor = "None";
                    if (group.Key != "Ungrouped")
                    {
                        var firstWithColor = group.FirstOrDefault(x => !string.IsNullOrEmpty(x.GroupColor) && x.GroupColor != "None");
                        if (firstWithColor != null) headerColor = firstWithColor.GroupColor;

                        if (headerColor != "None")
                        {
                            try
                            {
                                var groupColorBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(headerColor);
                                var dot = new Ellipse
                                {
                                    Width = 10,
                                    Height = 10,
                                    Fill = groupColorBrush,
                                    Margin = new Thickness(0, 0, 8, 0),
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                                Grid.SetColumn(dot, 0);
                                groupHeaderPanel.Children.Add(dot);
                            }
                            catch { }
                        }
                    }

                    var groupNameTxt = new TextBlock
                    {
                        Text = group.Key,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = group.Key == "Ungrouped" ? Brushes.Gray : new SolidColorBrush(Color.FromRgb(150, 200, 255)),
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        ToolTip = group.Key
                    };
                    Grid.SetColumn(groupNameTxt, 1);
                    groupHeaderPanel.Children.Add(groupNameTxt);

                    var expander = new Expander { IsExpanded = true, Margin = new Thickness(10, 0, 0, 5), Foreground = Brushes.White, Header = groupHeaderPanel };
                    var groupStack = new StackPanel();

                    var sortedGroup = group
                        .OrderByDescending(p => TrackingService.LastOnlinePlayers.Any(op => op.BMId == p.BMId))
                        .ThenByDescending(p => TrackingService.LastOnlinePlayers.FirstOrDefault(op => op.BMId == p.BMId)?.Duration ?? TimeSpan.Zero)
                        .ThenByDescending(p => p.Sessions.Count > 0 ? p.Sessions.Max(s => s.DisconnectTime ?? s.ConnectTime) : DateTime.MinValue)
                        .ToList();

                    foreach (var p in sortedGroup)
                    {
                        var onlineInfo = TrackingService.LastOnlinePlayers.FirstOrDefault(op => op.BMId == p.BMId);
                        p.IsOnline = onlineInfo != null;
                        p.PlayTimeStr = onlineInfo?.PlayTimeStr ?? "";

                        var contentControl = new ContentControl
                        {
                            Content = p,
                            ContentTemplate = template,
                            Margin = new Thickness(group.Key == "Ungrouped" ? 10 : 30, 0, 0, 0)
                        };

                        groupStack.Children.Add(contentControl);
                    }
                    expander.Content = groupStack;
                    ListTrackedPlayers.Children.Add(expander);
                }
            }
            if (players.Count == 0 && !string.IsNullOrEmpty(filter) && filter != "Filter players...")
            {
                ListTrackedPlayers.Children.Add(new TextBlock { Text = "No results found matching filter.", Margin = new Thickness(0, 20, 0, 0), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshTrackedPlayersList Crash: {ex.Message}");
        }
    }

    private void TxtTrackedFilter_GotFocus(object sender, RoutedEventArgs e) {
        if (TxtTrackedFilter.Text == "Filter players...") {
            TxtTrackedFilter.Text = "";
            TxtTrackedFilter.Foreground = Brushes.White;
        }
    }
    private void TxtTrackedFilter_LostFocus(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(TxtTrackedFilter.Text)) {
            TxtTrackedFilter.Text = "Filter players...";
            TxtTrackedFilter.Foreground = Brushes.Gray;
        }
    }
    private void TxtTrackedFilter_TextChanged(object sender, TextChangedEventArgs e) {
        if (TxtTrackedFilter.Text != "Filter players...") RefreshTrackedPlayersList(TxtTrackedFilter.Text);
    }

    private void BtnManageGroups_Click(object sender, RoutedEventArgs e) {
        if (ShowBulkGroupEditorDialog() == true) {
            RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
        }
    }

    private string? ShowInputBox(string prompt, string title, string defaultResponse)
    {
        var win = new Window
        {
            Title = title,
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = Brushes.White
        };

        var stack = new StackPanel { Margin = new Thickness(15) };
        stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        
        var input = new TextBox { Text = defaultResponse, Padding = new Thickness(5), Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)), Foreground = Brushes.White, BorderBrush = Brushes.Gray };
        input.SelectAll();
        stack.Children.Add(input);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var okBtn = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", Width = 70, IsCancel = true };

        string? result = null;
        okBtn.Click += (s, e) => { result = input.Text; win.DialogResult = true; };
        cancelBtn.Click += (s, e) => { win.DialogResult = false; };

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(btnPanel);

        win.Content = stack;
        input.Focus();
        
        win.ShowDialog();
        return result;
    }

    private (FrameworkElement UI, Func<string> Getter, Action<string> Setter) CreateColorSelector(string initialColor)
    {
        var combo = new ComboBox { 
            Margin = new Thickness(0, 5, 0, 10), 
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)FindResource("DarkComboBox")
        };

        var colors = new[] { "None", "Red", "Green", "Blue", "Yellow", "Purple", "Cyan", "Orange", "Pink", "White", "Gray" };
        
        foreach (var c in colors)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            var brush = c == "None" ? Brushes.Transparent : (SolidColorBrush)new BrushConverter().ConvertFromString(c);
            
            stack.Children.Add(new System.Windows.Shapes.Ellipse { 
                Width = 12, Height = 12, 
                Fill = brush, 
                Stroke = Brushes.Gray, 
                StrokeThickness = 1, 
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0) 
            });
            stack.Children.Add(new TextBlock { 
                Text = c, 
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            });
            
            combo.Items.Add(new ComboBoxItem { Content = stack, Tag = c });
        }

        Action<string> setter = (color) => {
            string colorToSelect = string.IsNullOrEmpty(color) ? "None" : color;
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag.ToString() == colorToSelect)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        };

        setter(initialColor);

        return (combo, () => {
            if (combo.SelectedItem is ComboBoxItem selectedItem)
                return selectedItem.Tag.ToString() == "None" ? "" : selectedItem.Tag.ToString();
            return "";
        }, setter);
    }

    private bool ShowBulkGroupEditorDialog()
    {
        var win = new Window
        {
            Title = "Create / Manage Group",
            Width = 450,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true
        };
        WindowBackdropHelper.Apply(win, WindowBackdropHelper.BackdropType.Mica);

        var root = new Border {
            Background = (Brush)FindResource("Surface"),
            CornerRadius = new CornerRadius(12),
            BorderBrush = (Brush)FindResource("CardBorder"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name Label
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Name Input
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Existing Groups
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Color Label
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Color Picker
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Players List
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        grid.Children.Add(new TextBlock { Text = "Manage Player Group", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,16) });

        var nameLabel = new TextBlock { Text = "Group Name", Margin = new Thickness(0, 10, 0, 8), Foreground = (Brush)FindResource("TextSubtle") };
        Grid.SetRow(nameLabel, 1);
        grid.Children.Add(nameLabel);

        var nameInput = new ui.TextBox { PlaceholderText = "Enter group name..." };
        Grid.SetRow(nameInput, 2);
        grid.Children.Add(nameInput);
        
        var existingPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        Grid.SetRow(existingPanel, 3);
        grid.Children.Add(existingPanel);

        var allPlayers = TrackingService.GetTrackedPlayers();
        var existingGroups = allPlayers.Where(p => !string.IsNullOrEmpty(p.GroupName)).Select(p => p.GroupName).Distinct().ToList();

        var listStack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var checkBoxes = new List<(CheckBox cb, TrackedPlayer p)>();
        foreach(var p in allPlayers.OrderBy(x => x.Name))
        {
            var cb = new CheckBox { 
                Content = $"{p.Name} ({p.BMId})", 
                IsChecked = false,
                Margin = new Thickness(0, 4, 0, 4)
            };
            checkBoxes.Add((cb, p));
            listStack.Children.Add(cb);
        }

        var colorSelector = CreateColorSelector("None");

        foreach(var g in existingGroups)
        {
            var gBtn = new ui.Button { 
                Content = g, 
                Margin = new Thickness(0,0,4,4), 
                Padding = new Thickness(8,4,8,4),
                Appearance = ui.ControlAppearance.Secondary
            };
            gBtn.Click += (s, e) => {
                nameInput.Text = g;
                var samplePlayer = allPlayers.FirstOrDefault(p => p.GroupName == g);
                if (samplePlayer != null) {
                    colorSelector.Setter(samplePlayer.GroupColor);
                    foreach(var (cb, p) in checkBoxes) {
                        cb.IsChecked = p.GroupName == g;
                    }
                }
            };
            existingPanel.Children.Add(gBtn);
        }

        var colorLabel = new TextBlock { Text = "Group Color", Margin = new Thickness(0, 16, 0, 8), Foreground = (Brush)FindResource("TextSubtle") };
        Grid.SetRow(colorLabel, 4);
        grid.Children.Add(colorLabel);

        Grid.SetRow(colorSelector.UI, 5);
        grid.Children.Add(colorSelector.UI);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 16, 0, 16) };
        scroll.Content = listStack;
        Grid.SetRow(scroll, 6);
        grid.Children.Add(scroll);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new ui.Button { Content = "Save Group", Width = 120, Margin = new Thickness(0, 0, 12, 0), Appearance = ui.ControlAppearance.Primary };
        var cancelBtn = new ui.Button { Content = "Cancel", Width = 90 };

        bool saved = false;
        okBtn.Click += (s, e) => { 
            var gName = nameInput.Text.Trim();
            var gColor = colorSelector.Getter();
            if (string.IsNullOrEmpty(gName)) {
                MessageBox.Show("Please enter a group name.");
                return;
            }
            
            foreach(var (cb, p) in checkBoxes)
            {
                if (cb.IsChecked == true)
                {
                    TrackingService.SetPlayerGroup(p.BMId, gName, gColor);
                }
                else if (p.GroupName == gName) 
                {
                    TrackingService.SetPlayerGroup(p.BMId, "", "");
                }
            }
            saved = true;
            win.DialogResult = true; 
        };
        cancelBtn.Click += (s, e) => { win.DialogResult = false; };

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        Grid.SetRow(btnPanel, 7);
        grid.Children.Add(btnPanel);

        root.Child = grid;
        win.Content = root;
        win.MouseLeftButtonDown += (s, e) => { try { win.DragMove(); } catch {} };
        nameInput.Loaded += (s, e) => { nameInput.Focus(); };
        
        win.ShowDialog();
        return saved;
    }

    private (string name, string color)? ShowGroupEditorDialog(TrackedPlayer player)
    {
        var win = new Window
        {
            Title = "Assign Group",
            Width = 400,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true
        };
        WindowBackdropHelper.Apply(win, WindowBackdropHelper.BackdropType.Mica);

        var root = new Border {
            Background = (Brush)FindResource("Surface"),
            CornerRadius = new CornerRadius(12),
            BorderBrush = (Brush)FindResource("CardBorder"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24)
        };
        
        var stack = new StackPanel();
        
        stack.Children.Add(new TextBlock { Text = $"Group Settings: {player.Name}", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,16) });
        
        stack.Children.Add(new TextBlock { Text = "Group Name", Foreground = (Brush)FindResource("TextSubtle") });
        var input = new ui.TextBox { 
            Text = player.GroupName, 
            PlaceholderText = "Enter group name..."
        };
        stack.Children.Add(input);

        stack.Children.Add(new TextBlock { Text = "Group Color", Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)FindResource("TextSubtle") });
        var colorSelector = CreateColorSelector(string.IsNullOrEmpty(player.GroupColor) ? "None" : player.GroupColor);
        stack.Children.Add(colorSelector.UI);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        
        (string, string)? result = null;

        var saveBtn = new ui.Button { Content = "Save Changes", Appearance = ui.ControlAppearance.Primary, Width = 130, Margin = new Thickness(0,0,12,0) };
        saveBtn.Click += (s, e) => { result = (input.Text.Trim(), colorSelector.Getter()); win.DialogResult = true; };

        var cancelBtn = new ui.Button { Content = "Cancel", Width = 90 };
        cancelBtn.Click += (s, e) => { win.DialogResult = false; };

        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(btnPanel);

        root.Child = stack;
        win.Content = root;
        win.MouseLeftButtonDown += (s, e) => { try { win.DragMove(); } catch {} };
        input.Loaded += (s, e) => { input.Focus(); input.SelectAll(); };
        
        win.ShowDialog();
        return result;
    }

    private void ShowTrackingAnalysisWindow(string? bmId = null)
    {
        var html = TrackingService.GetAnalysisReport(bmId);

        var win = new Window
        {
            Title = "Player Activity Analytics & Forecasts",
            Width = 900,
            Height = 750,
            Background = new SolidColorBrush(Color.FromRgb(18, 20, 23)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
        };

        var grid = new Grid();
        var wv = new WebView2 { Margin = new Thickness(0) };
        grid.Children.Add(wv);
        win.Content = grid;
        
        win.Loaded += async (s, e) =>
        {
            try 
            {
                var dataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RustPlusDesk", "WebView2_Report");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: dataPath);
                await wv.EnsureCoreWebView2Async(env);
                wv.NavigateToString(html);
            } 
            catch (Exception ex) 
            {
                win.Content = new ScrollViewer 
                { 
                   Content = new TextBlock 
                   { 
                      Text = "Error loading analytics view: " + ex.Message + "\n\nEnsure WebView2 Runtime is installed.", 
                      Foreground = Brushes.White,
                      TextWrapping = TextWrapping.Wrap,
                      Margin = new Thickness(20) 
                   } 
                };
            }
        };

        win.Show();
    }

    private void ShowPlayerAnalysis(string bmId, string name)
    {
        ShowTrackingAnalysisWindow(bmId);
    }
}
