using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class NotificationsTabContent : UserControl
    {
        private ICollectionView? _notificationsView;

        public NotificationsTabContent()
        {
            InitializeComponent();
            
            // Wire up notification binding
            LstNotifications.ItemsSource = NotificationCenterService.Notifications;
            _notificationsView = CollectionViewSource.GetDefaultView(NotificationCenterService.Notifications);
            
            if (_notificationsView != null)
            {
                _notificationsView.Filter = FilterNotification;
                // Sort by Timestamp descending (most recent first)
                _notificationsView.SortDescriptions.Clear();
                _notificationsView.SortDescriptions.Add(new SortDescription("Timestamp", ListSortDirection.Descending));
            }

            // Listen to collection changes to update empty state
            NotificationCenterService.Notifications.CollectionChanged += (s, e) => UpdateEmptyState();
            UpdateEmptyState();
        }

        private bool FilterNotification(object item)
        {
            if (item is not RustPlusNotification notif) return false;

            // 1) Search filter
            string search = TxtSearch.Text.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                bool titleMatch = notif.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false;
                bool msgMatch = notif.Message?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false;
                bool serverMatch = notif.ServerName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false;
                if (!titleMatch && !msgMatch && !serverMatch)
                {
                    return false;
                }
            }

            // 2) Category filter
            if (RadAlarms.IsChecked == true)
            {
                return string.Equals(notif.Type, "Alarm", StringComparison.OrdinalIgnoreCase);
            }
            if (RadDeaths.IsChecked == true)
            {
                return string.Equals(notif.Type, "Death", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _notificationsView?.Refresh();
            UpdateEmptyState();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _notificationsView?.Refresh();
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_notificationsView == null || _notificationsView.Cast<object>().Any())
                {
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                    LstNotifications.Visibility = Visibility.Visible;
                }
                else
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    LstNotifications.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void LstNotifications_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstNotifications.SelectedItem is RustPlusNotification notif)
            {
                NotificationCenterService.MarkAsRead(notif.Id);
                // Clear selection so it is not highlighted permanently
                LstNotifications.SelectedItem = null;
            }
        }

        private void BtnMarkAllRead_Click(object sender, RoutedEventArgs e)
        {
            NotificationCenterService.MarkAllAsRead();
            _notificationsView?.Refresh();
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(Properties.Resources.ClearNotificationsConfirm, Properties.Resources.ClearNotificationsConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                NotificationCenterService.ClearHistory();
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                NotificationCenterService.DeleteNotification(id);
            }
        }

        private async void BtnServerTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RustPlusNotification notif)
            {
                if (string.IsNullOrEmpty(notif.ServerIp) || !notif.ServerPort.HasValue) return;

                var mainWin = Application.Current.MainWindow as MainWindow;
                if (mainWin?.ViewModel?.Servers == null) return;

                var profile = mainWin.ViewModel.Servers.FirstOrDefault(s => 
                    s.Host == notif.ServerIp && s.Port == notif.ServerPort.Value);

                if (profile != null)
                {
                    // Select server profile
                    mainWin.ViewModel.Selected = profile;
                    
                    // Trigger connection flow
                    await mainWin.PerformConnectAsync(silent: false, showBusy: true);
                }
                else
                {
                    MessageBox.Show(string.Format(Properties.Resources.ServerNotFoundMessage, notif.ServerIp, notif.ServerPort.Value), Properties.Resources.ServerNotFoundTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}
