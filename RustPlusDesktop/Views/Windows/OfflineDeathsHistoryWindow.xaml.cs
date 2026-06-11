using System;
using System.Windows;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views.Windows
{
    public partial class OfflineDeathsHistoryWindow : Window
    {
        public OfflineDeathsHistoryWindow()
        {
            InitializeComponent();
            
            // Apply theme styling using helper (if exists) or fallback
            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
            }
            catch { }
            
            LoadHistory();
        }

        private void LoadHistory()
        {
            var history = TrackingService.OfflineDeathHistory;
            ListDeaths.ItemsSource = null;
            ListDeaths.ItemsSource = history;

            if (history == null || history.Count == 0)
            {
                TxtEmptyState.Visibility = Visibility.Visible;
                ListDeaths.Visibility = Visibility.Collapsed;
                BtnClearHistory.IsEnabled = false;
            }
            else
            {
                TxtEmptyState.Visibility = Visibility.Collapsed;
                ListDeaths.Visibility = Visibility.Visible;
                BtnClearHistory.IsEnabled = true;
            }
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to clear the offline death log?", "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                TrackingService.ClearOfflineDeathHistory();
                LoadHistory();
            }
        }
    }
}
