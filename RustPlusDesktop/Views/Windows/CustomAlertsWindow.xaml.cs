using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views.Windows
{
    public partial class CustomAlertsWindow : Window
    {
        public List<AlertModel> Alerts { get; set; } = new();

        public CustomAlertsWindow()
        {
            InitializeComponent();
            LoadAlerts();
            AlertsItemsControl.ItemsSource = Alerts;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void LoadAlerts()
        {
            var keys = new[]
            {
                "AlertOilRigTriggered",
                "AlertCrateUnlocksIn10Min",
                "AlertCrateUnlocksIn5Min",
                "AlertAlarmTriggered",
                "AlertDeepSeaUp",
                "AlertNewShop",
                "AlertSuspiciousShop",
                "AlertShopMatch",
                "AlertCargoSpawned",
                "AlertCargoDocked",
                "AlertCargoExpectedDock",
                "AlertCargoDeparting",
                "AlertEventSpawned",
                "AlertHeliCrashFalseAlarm",
                "AlertHeliShotDown",
                "AlertPlayerOnlineWithPos",
                "AlertPlayerOffline",
                "AlertPlayerDied",
                "AlertPlayerRespawned",
                "AlertTrackingOnline",
                "AlertTrackingOffline",
                "AlertTrackingRenamed"
            };

            Alerts = new List<AlertModel>();
            foreach (var key in keys)
            {
                Alerts.Add(new AlertModel(key));
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AlertModel model)
            {
                if (string.IsNullOrWhiteSpace(model.CurrentText))
                {
                    AlertTemplateService.RemoveOverride(model.Key);
                    model.CurrentText = AlertTemplateService.GetAlertTemplate(model.Key);
                }
                else
                {
                    AlertTemplateService.SetOverride(model.Key, model.CurrentText);
                }

                // Visual Feedback: Flash Green
                var tb = FindTextBoxInRow(btn);
                if (tb != null)
                {
                    FlashTextBox(tb, System.Windows.Media.Color.FromArgb(255, 30, 130, 76)); // Soft emerald green
                }
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AlertModel model)
            {
                AlertTemplateService.RemoveOverride(model.Key);
                model.CurrentText = AlertTemplateService.GetAlertTemplate(model.Key);

                // Visual Feedback: Flash Red, then Green
                var tb = FindTextBoxInRow(btn);
                if (tb != null)
                {
                    FlashTextBox(tb, System.Windows.Media.Color.FromArgb(255, 150, 40, 27), System.Windows.Media.Color.FromArgb(255, 30, 130, 76)); // Soft Red then soft Green
                }
            }
        }

        private TextBox? FindTextBoxInRow(Button btn)
        {
            DependencyObject parent = btn;
            while (parent != null && parent is not Grid)
            {
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }

            if (parent != null)
            {
                return FindTextBoxRecursive(parent);
            }
            return null;
        }

        private TextBox? FindTextBoxRecursive(DependencyObject parent)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox tb)
                {
                    return tb;
                }
                var result = FindTextBoxRecursive(child);
                if (result != null) return result;
            }
            return null;
        }

        private async void FlashTextBox(TextBox textBox, System.Windows.Media.Color firstColor, System.Windows.Media.Color? secondColor = null)
        {
            var originalBrush = textBox.Background;

            // Flash first color
            textBox.Background = new System.Windows.Media.SolidColorBrush(firstColor);
            await System.Threading.Tasks.Task.Delay(250);

            if (secondColor.HasValue)
            {
                // Flash second color
                textBox.Background = new System.Windows.Media.SolidColorBrush(secondColor.Value);
                await System.Threading.Tasks.Task.Delay(250);
            }

            // Restore original background brush
            textBox.Background = originalBrush;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class AlertModel : INotifyPropertyChanged
    {
        public string Key { get; }
        public string Name { get; }
        public string Description { get; }
        public string Variables { get; }

        private string _currentText = string.Empty;
        public string CurrentText
        {
            get => _currentText;
            set
            {
                if (_currentText != value)
                {
                    _currentText = value;
                    OnPropertyChanged();
                }
            }
        }

        public AlertModel(string key)
        {
            Key = key;
            Name = Properties.Resources.ResourceManager.GetString(key + "Name") ?? key;
            Description = Properties.Resources.ResourceManager.GetString(key + "Desc") ?? string.Empty;
            
            string varsText = Properties.Resources.ResourceManager.GetString(key + "Vars") ?? string.Empty;
            string varsLabelPattern = Properties.Resources.ResourceManager.GetString("VariablesLabel") ?? "Variables: {0}";
            Variables = string.Format(varsLabelPattern, varsText);
            
            _currentText = AlertTemplateService.GetAlertTemplate(key);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
