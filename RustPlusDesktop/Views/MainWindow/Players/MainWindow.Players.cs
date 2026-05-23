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
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private Point _trackedGroupDragStartPoint;
    private Expander? _draggedTrackedGroup;

    private void BtnHowToTrack_Click(object sender, RoutedEventArgs e)
    {
        HowToTrackWindow.Show(this);
    }

    private void BtnViewTracked_Click(object sender, RoutedEventArgs e)
    {
        var player = ((sender as FrameworkElement)?.DataContext as TrackedPlayer);
        var bmId = (sender as FrameworkElement)?.Tag as string ?? player?.BMId;
        if (!string.IsNullOrEmpty(bmId))
        {
            ShowTrackingAnalysisWindow(bmId);
        }
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
        try
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
                    if (TxtTrackingStatus != null)
                    {
                        TxtTrackingStatus.Text = Properties.Resources.AddPlayersToStartTracking;
                        TxtTrackingStatus.Foreground = Brushes.Gray;
                        TxtTrackingStatus.FontStyle = FontStyles.Italic;
                    }
                }
                else
                {
                    if (TxtTrackingStatus != null)
                    {
                        bool hasRealTracked = TrackingService.GetTrackedPlayers().Any(p => !p.IsBMOnly);
                        if (hasRealTracked)
                        {
                            TxtTrackingStatus.Text = TrackingService.IsTracking ? Properties.Resources.TrackingActiveStatus : Properties.Resources.TrackingIdleStatus;
                            TxtTrackingStatus.Foreground = TrackingService.IsTracking ? Brushes.White : Brushes.Gray;
                        }
                        else
                        {
                            // Only BM shortcuts — no UDP polling needed
                            TxtTrackingStatus.Text = Properties.Resources.BmShortcuts;
                            TxtTrackingStatus.Foreground = Brushes.Gray;
                        }
                        TxtTrackingStatus.FontStyle = FontStyles.Normal;
                    }
                }
                
                if (TrackingService.LastPullTime.HasValue && anyTracked)
                {
                    if (TxtLastPull != null) TxtLastPull.Text = string.Format(Properties.Resources.LastPull, TrackingService.LastPullTime.Value.ToString("HH:mm:ss"));
                }
                else
                {
                    if (TxtLastPull != null) TxtLastPull.Text = string.Format(Properties.Resources.LastPull, "--:--");
                }
            });
        }
        catch { }
    }

    private void OnServerInfoUpdated(string description)
    {
        try
        {
            Dispatcher.Invoke(() => {
                if (_vm.Selected != null && string.IsNullOrWhiteSpace(_vm.Selected.Description))
                {
                    _vm.Selected.Description = description;
                }
            });
        }
        catch { }
    }

    private void RefreshOnlinePlayersList()
    {
        try
        {
            var players = TrackingService.LastOnlinePlayers;

            // Show filter box when there are players, hide it when empty
            if (TxtOnlineFilter != null)
                TxtOnlineFilter.Visibility = players.Count > 0 ? Visibility.Visible : Visibility.Collapsed;


            var filterTxt = TxtOnlineFilter?.Text;
            if (!string.IsNullOrWhiteSpace(filterTxt))
            {
                players = players.Where(p => p.Name.Contains(filterTxt, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (ListOnlinePlayers != null)
            {
                ListOnlinePlayers.ItemsSource = null;
                ListOnlinePlayers.ItemsSource = players;
            }

            if (TrackingService.LastOnlinePlayers.Count == 0)
            {
                if (ListOnlinePlayers != null) ListOnlinePlayers.Visibility = Visibility.Collapsed;
                if (TxtOnlinePlayersStatus != null) TxtOnlinePlayersStatus.Text = TrackingService.StatusMessage;
                if (PnlOnlineStatus != null) PnlOnlineStatus.Visibility = Visibility.Visible;
                
                // Only show the spinner when actively polling a server.
                // An empty StatusMessage with no server means "not connected" — don't spin forever.
                bool hasServer = !string.IsNullOrEmpty(TrackingService.LastServer.host);
                bool isWorking = hasServer && (
                                 string.IsNullOrEmpty(TrackingService.StatusMessage) ||
                                 TrackingService.StatusMessage.Contains("Fetching") ||
                                 TrackingService.StatusMessage.Contains("Looking") ||
                                 TrackingService.StatusMessage.Contains("Auto-Discovering"));

                if (PbOnlineLoading != null) PbOnlineLoading.Visibility = isWorking ? Visibility.Visible : Visibility.Collapsed;
                
                if (PnlManualTrack != null) PnlManualTrack.Visibility = Visibility.Visible;
            }
            else
            {
                if (ListOnlinePlayers != null) ListOnlinePlayers.Visibility = Visibility.Visible;
                if (PnlOnlineStatus != null) PnlOnlineStatus.Visibility = Visibility.Collapsed;
                if (PnlManualTrack != null) PnlManualTrack.Visibility = Visibility.Visible;
            }


        }
        catch { }
    }

    private void TxtOnlineFilter_TextChanged(object sender, TextChangedEventArgs e) {
        RefreshOnlinePlayersList();
    }

    private async void BtnShowOnline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Name == "BtnShowOnline")
        {
            MainTabs.SelectedIndex = 3; // Players tab
        }

        if (_vm.Selected == null || string.IsNullOrEmpty(_vm.Selected.Host))
        {
            TxtOnlinePlayersStatus.Text = Properties.Resources.ConnectToLoadPlayers;
            PnlOnlineStatus.Visibility = Visibility.Visible;
            PbOnlineLoading.Visibility = Visibility.Collapsed;
            ListOnlinePlayers.ItemsSource = null;
            return;
        }

        TxtOnlinePlayersStatus.Text = Properties.Resources.FetchingPlayersSteam;
        PnlOnlineStatus.Visibility = Visibility.Visible;
        PbOnlineLoading.Visibility = Visibility.Visible;
        if (PnlManualTrack != null) PnlManualTrack.Visibility = Visibility.Visible;
        ListOnlinePlayers.ItemsSource = null;
        ListOnlinePlayers.Visibility = Visibility.Collapsed;

        try
        {
            await TrackingService.FetchOnlinePlayersNowAsync();
        }
        catch (Exception ex)
        {
            TxtOnlinePlayersStatus.Text = $"Error: {ex.Message}";
            PnlOnlineStatus.Visibility = Visibility.Visible;
            PbOnlineLoading.Visibility = Visibility.Collapsed;
            if (PnlManualTrack != null) PnlManualTrack.Visibility = Visibility.Visible;
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



    private async void BtnAddManual_Click(object sender, RoutedEventArgs e)
    {
        var bmId = TxtManualBMId.Text?.Trim();
        if (string.IsNullOrEmpty(bmId)) return;

        TxtManualBMId.IsEnabled = false;
        BtnAddManual.Content = "...";
        
        var name = await TrackingService.FetchPlayerNameAsync(bmId);
        var lastSession = await TrackingService.FetchPlayerLastSessionAsync(bmId);
        
        var serverName = TrackingService.LastServer.name;
        if (string.IsNullOrEmpty(serverName)) serverName = _vm.Selected?.Name ?? "Manual Add";

        TrackingService.TrackPlayer(bmId, name, serverName, lastSession);
        
        TxtManualBMId.Text = "";
        TxtManualBMId.IsEnabled = true;
        BtnAddManual.Content = Properties.Resources.TrackID;
        
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
            if (!string.IsNullOrEmpty(filter))
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
                var serverHeader = new TextBlock
                {
                    Text = serverGrp.Key,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                    Margin = new Thickness(0, 15, 0, 5),
                    FontSize = 14,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = serverGrp.Key
                };
                ListTrackedPlayers.Children.Add(serverHeader);

                var order = TrackingService.GetGroupOrder(serverGrp.Key);
                var subgroups = serverGrp.GroupBy(p => string.IsNullOrEmpty(p.GroupName) ? "Ungrouped" : p.GroupName)
                    .OrderBy(g => {
                        int idx = order.IndexOf(g.Key);
                        if (idx >= 0) return idx;
                        return g.Key == "Ungrouped" ? 1000 : 500;
                    })
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

                    var isExp = TrackingService.GetGroupState(serverGrp.Key, group.Key);
                    var expander = new Expander { 
                        IsExpanded = isExp, 
                        Margin = new Thickness(10, 0, 0, 5), 
                        Foreground = Brushes.White, 
                        Header = groupHeaderPanel,
                        AllowDrop = true,
                        Tag = (serverGrp.Key, group.Key)
                    };
                    
                    expander.Expanded += (s, e) => TrackingService.SetGroupState(serverGrp.Key, group.Key, true);
                    expander.Collapsed += (s, e) => TrackingService.SetGroupState(serverGrp.Key, group.Key, false);
                    
                    // Drag & Drop
                    expander.PreviewMouseLeftButtonDown += TrackedGroup_PreviewMouseLeftButtonDown;
                    expander.PreviewMouseMove += TrackedGroup_PreviewMouseMove;
                    expander.DragOver += TrackedGroup_DragOver;
                    expander.Drop += TrackedGroup_Drop;

                    var groupStack = new StackPanel();

                    var sortedGroup = group
                        .OrderByDescending(p => TrackingService.LastOnlinePlayers.Any(op => op.BMId == p.BMId))
                        .ThenByDescending(p => TrackingService.LastOnlinePlayers.FirstOrDefault(op => op.BMId == p.BMId)?.Duration ?? TimeSpan.Zero)
                        .ThenByDescending(p => p.Sessions.Count > 0 ? p.Sessions.Max(s => s.DisconnectTime ?? s.ConnectTime) : DateTime.MinValue)
                        .ToList();

                    foreach (var p in sortedGroup)
                    {
                        p.IsOnline = p.Sessions.Count > 0 && !p.Sessions.Last().DisconnectTime.HasValue;
                        if (p.IsOnline)
                        {
                            var d = DateTime.UtcNow - p.Sessions.Last().ConnectTime;
                            p.PlayTimeStr = $"{(int)d.TotalHours:D2}:{d.Minutes:D2}";
                        }
                        else
                        {
                            p.PlayTimeStr = "";
                        }

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
            if (players.Count == 0 && !string.IsNullOrEmpty(filter))
            {
                ListTrackedPlayers.Children.Add(new TextBlock { Text = "No results found matching filter.", Margin = new Thickness(0, 20, 0, 0), Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshTrackedPlayersList Crash: {ex.Message}");
        }
    }

    private void TrackedGroup_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _trackedGroupDragStartPoint = e.GetPosition(null);
        if (sender is Expander exp)
        {
            _draggedTrackedGroup = exp;
        }
    }

    private void TrackedGroup_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedTrackedGroup != null)
        {
            Point currentPosition = e.GetPosition(null);
            if (Math.Abs(currentPosition.X - _trackedGroupDragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPosition.Y - _trackedGroupDragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(_draggedTrackedGroup, _draggedTrackedGroup, DragDropEffects.Move);
            }
        }
    }

    private void TrackedGroup_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Expander)))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void TrackedGroup_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_draggedTrackedGroup == null || !e.Data.GetDataPresent(typeof(Expander)))
            return;

        var targetExpander = sender as Expander;
        if (targetExpander == null || ReferenceEquals(_draggedTrackedGroup, targetExpander))
            return;

        if (_draggedTrackedGroup.Tag is not (string sourceServer, string sourceGroup) ||
            targetExpander.Tag is not (string targetServer, string targetGroup))
            return;

        if (sourceServer != targetServer) return; // Only reorder within same server

        var order = TrackingService.GetGroupOrder(sourceServer);
        if (order.Count == 0)
        {
            // Initialize order if empty
            var players = TrackingService.GetTrackedPlayers();
            order = players.Where(p => (string.IsNullOrEmpty(p.LastServerName) ? "Global / Legacy" : p.LastServerName) == sourceServer)
                           .Select(p => string.IsNullOrEmpty(p.GroupName) ? "Ungrouped" : p.GroupName)
                           .Distinct()
                           .ToList();
        }

        int sourceIdx = order.IndexOf(sourceGroup);
        int targetIdx = order.IndexOf(targetGroup);

        if (sourceIdx >= 0 && targetIdx >= 0)
        {
            order.RemoveAt(sourceIdx);
            order.Insert(targetIdx, sourceGroup);
            TrackingService.SetGroupOrder(sourceServer, order);
            RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
        }
        else if (targetIdx >= 0)
        {
            // Source wasn't in order yet (maybe it was just created)
            order.Insert(targetIdx, sourceGroup);
            TrackingService.SetGroupOrder(sourceServer, order);
            RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
        }

        _draggedTrackedGroup = null;
    }

    private void TxtTrackedFilter_TextChanged(object sender, TextChangedEventArgs e) {
        RefreshTrackedPlayersList(TxtTrackedFilter?.Text ?? "");
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
            Title = Properties.Resources.ManageGroups,
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

        var nameInput = new WpfUi.TextBox { PlaceholderText = "Enter group name..." };
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
            var gBtn = new WpfUi.Button { 
                Content = g, 
                Margin = new Thickness(0,0,4,4), 
                Padding = new Thickness(8,4,8,4),
                Appearance = WpfUi.ControlAppearance.Secondary
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

        var colorLabel = new TextBlock { Text = Properties.Resources.GroupColor, Margin = new Thickness(0, 16, 0, 8), Foreground = (Brush)FindResource("TextSubtle") };
        Grid.SetRow(colorLabel, 4);
        grid.Children.Add(colorLabel);

        Grid.SetRow(colorSelector.UI, 5);
        grid.Children.Add(colorSelector.UI);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 16, 0, 16) };
        scroll.Content = listStack;
        Grid.SetRow(scroll, 6);
        grid.Children.Add(scroll);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new WpfUi.Button { Content = "Save Group", Width = 120, Margin = new Thickness(0, 0, 12, 0), Appearance = WpfUi.ControlAppearance.Primary };
        var cancelBtn = new WpfUi.Button { Content = "Cancel", Width = 90 };

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
        var input = new WpfUi.TextBox { 
            Text = player.GroupName, 
            PlaceholderText = "Enter group name..."
        };
        stack.Children.Add(input);

        stack.Children.Add(new TextBlock { Text = "Group Color", Margin = new Thickness(0, 8, 0, 0), Foreground = (Brush)FindResource("TextSubtle") });
        var colorSelector = CreateColorSelector(string.IsNullOrEmpty(player.GroupColor) ? "None" : player.GroupColor);
        stack.Children.Add(colorSelector.UI);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        
        (string, string)? result = null;

        var saveBtn = new WpfUi.Button { Content = "Save Changes", Appearance = WpfUi.ControlAppearance.Primary, Width = 130, Margin = new Thickness(0,0,12,0) };
        saveBtn.Click += (s, e) => { result = (input.Text.Trim(), colorSelector.Getter()); win.DialogResult = true; };

        var cancelBtn = new WpfUi.Button { Content = "Cancel", Width = 90 };
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
        try
        {
            var html = TrackingService.GetAnalysisReport(bmId);

            var win = new Window
            {
                Title = "Player Activity Analytics & Forecasts",
                Width = 900,
                Height = 750,
                Background = new SolidColorBrush(Color.FromRgb(18, 20, 23)),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
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
                    if (!win.IsLoaded) return;
                    await wv.EnsureCoreWebView2Async(env);
                    if (wv.CoreWebView2 != null) 
                    {
                        wv.CoreWebView2.NewWindowRequested += (sender, args) =>
                        {
                            args.Handled = true;
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = args.Uri,
                                    UseShellExecute = true
                                });
                            }
                            catch { }
                        };
                        wv.NavigateToString(html);
                    }
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
        catch { }
    }

    private void ShowPlayerAnalysis(string bmId, string name)
    {
        ShowTrackingAnalysisWindow(bmId);
    }

    private Window? _playersPopoutWin;

    private void BtnPopoutPlayers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_playersPopoutWin != null)
            {
                _playersPopoutWin.Activate();
                return;
            }

            _playersPopoutWin = BuildPlayersPopoutWindow();
            _playersPopoutWin.Closed += (_, _) => _playersPopoutWin = null;
            _playersPopoutWin.Show();
        }
        catch (Exception ex)
        {
            _playersPopoutWin = null;
            MessageBox.Show($"Failed to open players window:\n\n{ex.GetType().Name}: {ex.Message}\n\nStack:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Window BuildPlayersPopoutWindow()
    {
        var win = new Window
        {
            Title = "Players",
            Width = 500,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(30, 32, 36)),
        };

        var tabControl = new TabControl
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(8),
        };

        // === Online Tab ===
        var onlineScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var onlineStack = new StackPanel { Margin = new Thickness(8) };
        onlineScroll.Content = onlineStack;

        var onlineTab = new TabItem { Header = "Online", Content = onlineScroll };

        // === Tracked Tab ===
        var trackedScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var trackedStack = new StackPanel { Margin = new Thickness(8) };
        trackedScroll.Content = trackedStack;

        var trackedTab = new TabItem { Header = "Tracked", Content = trackedScroll };

        tabControl.Items.Add(onlineTab);
        tabControl.Items.Add(trackedTab);

        win.Content = tabControl;

        Action? onUpdated = null;
        onUpdated = () =>
        {
            try 
            { 
                win?.Dispatcher.BeginInvoke(new Action(() => { 
                    try 
                    {
                        if (win == null || !win.IsLoaded) return;
                        PopulateOnlinePlayers(onlineStack, bmId => ShowTrackingAnalysisWindow(bmId)); 
                        PopulateTrackedPlayers(trackedStack, bmId => ShowTrackingAnalysisWindow(bmId)); 
                    } 
                    catch { }
                })); 
            } 
            catch { }
        };

        win.Loaded += (_, _) =>
        {
            try { PopulateOnlinePlayers(onlineStack, bmId => ShowTrackingAnalysisWindow(bmId)); } catch { }
            try { PopulateTrackedPlayers(trackedStack, bmId => ShowTrackingAnalysisWindow(bmId)); } catch { }
            TrackingService.OnOnlinePlayersUpdated += onUpdated;
        };

        win.Closed += (_, _) =>
        {
            TrackingService.OnOnlinePlayersUpdated -= onUpdated;
        };

        return win;
    }

    private static void PopulateOnlinePlayers(StackPanel stack, Action<string>? showAnalysis = null)
    {
        try
        {
            stack.Children.Clear();
            var players = TrackingService.LastOnlinePlayers.ToList(); // Snapshot
            if (players.Count == 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = TrackingService.StatusMessage,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0),
                });
                return;
            }
            foreach (var p in players)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var hp = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = new Ellipse
                {
                    Width = 8, Height = 8,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = p.IsTracked
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0x66))
                        : (SolidColorBrush)new BrushConverter().ConvertFromString("#60CDFF"),
                };
                hp.Children.Add(dot);

                var name = new TextBlock
                {
                    Text = p.Name,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 160,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = p.Name,
                };
                hp.Children.Add(name);
                row.Children.Add(hp);

                if (!string.IsNullOrEmpty(p.PlayTimeStr))
                {
                    var pt = new TextBlock
                    {
                        Text = p.PlayTimeStr,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    };
                    Grid.SetColumn(pt, 1);
                    row.Children.Add(pt);
                }

                var btnTrack = new WpfUi.Button
                {
                    Content = p.IsTracked ? "Details" : "Track",
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 11,
                    Appearance = p.IsTracked ? WpfUi.ControlAppearance.Primary : WpfUi.ControlAppearance.Secondary,
                    Tag = p.BMId,
                };
                Grid.SetColumn(btnTrack, 2);
                row.Children.Add(btnTrack);
                string capturedBmId = p.BMId;
                string capturedName = p.Name;
                btnTrack.Click += (_, _) =>
                {
                    if (TrackingService.GetTrackedPlayers().Any(tp => tp.BMId == capturedBmId))
                    {
                        showAnalysis?.Invoke(capturedBmId);
                    }
                    else
                    {
                        var srvName = TrackingService.LastServer.name ?? "Unknown";
                        TrackingService.TrackPlayer(capturedBmId, capturedName, srvName);
                        btnTrack.Content = "Details";
                        btnTrack.Appearance = WpfUi.ControlAppearance.Primary;
                    }
                };

                stack.Children.Add(row);
            }
        }
        catch { }
    }

    private static void PopulateTrackedPlayers(StackPanel stack, Action<string>? showAnalysis = null)
    {
        try
        {
            stack.Children.Clear();
            var players = TrackingService.GetTrackedPlayers();
            var onlineSnapshot = TrackingService.LastOnlinePlayers.ToList(); // Snapshot
            var serversGrouped = players.GroupBy(p => string.IsNullOrEmpty(p.LastServerName) ? "Global / Legacy" : p.LastServerName);
            foreach (var serverGrp in serversGrouped)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = serverGrp.Key,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                    Margin = new Thickness(0, 15, 0, 5),
                    FontSize = 14,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = serverGrp.Key,
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
                                    Width = 10, Height = 10, Fill = groupColorBrush,
                                    Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
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
                        ToolTip = group.Key,
                    };
                    Grid.SetColumn(groupNameTxt, 1);
                    groupHeaderPanel.Children.Add(groupNameTxt);

                    var isExp = TrackingService.GetGroupState(serverGrp.Key, group.Key);
                    var expander = new Expander { IsExpanded = isExp, Margin = new Thickness(10, 0, 0, 5), Foreground = Brushes.White, Header = groupHeaderPanel };
                    expander.Expanded += (s, e) => TrackingService.SetGroupState(serverGrp.Key, group.Key, true);
                    expander.Collapsed += (s, e) => TrackingService.SetGroupState(serverGrp.Key, group.Key, false);

                    var groupStack = new StackPanel();

                    var sortedGroup = group
                        .OrderByDescending(p => onlineSnapshot.Any(op => op.BMId == p.BMId))
                        .ThenByDescending(p => onlineSnapshot.FirstOrDefault(op => op.BMId == p.BMId)?.Duration ?? TimeSpan.Zero)
                        .ThenByDescending(p => p.Sessions.Count > 0 ? p.Sessions.Max(s => s.DisconnectTime ?? s.ConnectTime) : DateTime.MinValue)
                        .ToList();

                    foreach (var p in sortedGroup)
                    {
                        p.IsOnline = p.Sessions.Count > 0 && !p.Sessions.Last().DisconnectTime.HasValue;
                        if (p.IsOnline)
                        {
                            var d = DateTime.UtcNow - p.Sessions.Last().ConnectTime;
                            p.PlayTimeStr = $"{(int)d.TotalHours:D2}:{d.Minutes:D2}";
                        }
                        else
                        {
                            p.PlayTimeStr = "";
                        }

                        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var stack2 = new StackPanel { Orientation = Orientation.Horizontal };
                        var sDot = new Ellipse
                        {
                            Width = 8, Height = 8,
                            Margin = new Thickness(0, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Fill = p.IsOnline ? new SolidColorBrush(Color.FromRgb(98, 211, 139)) : new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                        };
                        stack2.Children.Add(sDot);

                        var nameBlock = new TextBlock
                        {
                            Text = p.Name,
                            FontSize = 13,
                            Foreground = p.IsOnline ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        stack2.Children.Add(nameBlock);
                        row.Children.Add(stack2);

                        if (p.IsOnline)
                        {
                            var playTime = new TextBlock
                            {
                                Text = p.PlayTimeStr,
                                FontSize = 10,
                                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin = new Thickness(8, 0, 0, 0),
                            };
                            Grid.SetColumn(playTime, 1);
                            row.Children.Add(playTime);
                        }

                        // Action button: BM-only → "View on BM" opens BM browser; native → "View" opens Analysis
                        string capturedBmId2 = p.BMId;
                        bool capturedIsBmOnly = p.IsBMOnly;
                        var actionBtn = new WpfUi.Button
                        {
                            Content = capturedIsBmOnly ? "View on BM" : "View",
                            Padding = new Thickness(6, 2, 6, 2),
                            FontSize = 11,
                            Appearance = capturedIsBmOnly ? WpfUi.ControlAppearance.Secondary : WpfUi.ControlAppearance.Primary,
                            Margin = new Thickness(6, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        actionBtn.Click += async (_, _) =>
                        {
                            if (capturedIsBmOnly)
                            {
                                var bmUrl = $"https://www.battlemetrics.com/players/{capturedBmId2}";
                                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bmUrl) { UseShellExecute = true }); }
                                catch { }
                            }
                            else
                            {
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                    showAnalysis?.Invoke(capturedBmId2));
                            }
                        };
                        Grid.SetColumn(actionBtn, 2);
                        row.Children.Add(actionBtn);

                        groupStack.Children.Add(row);

                    }
                    expander.Content = groupStack;
                    stack.Children.Add(expander);
                }
            }
        }
        catch { }
    }
}
