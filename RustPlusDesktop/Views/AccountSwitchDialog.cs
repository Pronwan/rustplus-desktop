using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public class AccountSwitchDialog : Window
    {
        public string? SelectedProfileId { get; private set; }

        public AccountSwitchDialog(List<Profile> profiles, Profile? currentProfile = null)
        {
            Title = Properties.Resources.SwitchAccount;
            Width = 320;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 30));
            Foreground = System.Windows.Media.Brushes.White;

            var grid = new StackPanel { Margin = new Thickness(20) };

            var label = new TextBlock
            {
                Text = Properties.Resources.ChooseAccount,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 14
            };
            grid.Children.Add(label);

            var comboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
            ComboBoxItem? itemToSelect = null;
            foreach (var p in profiles)
            {
                var item = new ComboBoxItem { Content = p.DisplayName, Tag = p.Id };
                comboBox.Items.Add(item);
                if (currentProfile != null && p.Id == currentProfile.Id)
                    itemToSelect = item;
            }
            comboBox.SelectedItem = itemToSelect ?? (comboBox.Items.Count > 0 ? comboBox.Items[0] as ComboBoxItem : null);
            grid.Children.Add(comboBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnCancel = new Button
            {
                Content = Properties.Resources.Cancel,
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Content = Properties.Resources.Switch,
                Width = 80,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnOk.Click += (s, e) =>
            {
                if (comboBox.SelectedItem is ComboBoxItem item)
                {
                    SelectedProfileId = item.Tag as string;
                }
                DialogResult = true;
                Close();
            };
            btnPanel.Children.Add(btnOk);

            grid.Children.Add(btnPanel);
            Content = grid;
        }
    }
}
