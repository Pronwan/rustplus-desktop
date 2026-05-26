using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public class BackupProfileDialog : Window
    {
        public List<string> SelectedProfileIds { get; private set; } = new();

        public BackupProfileDialog(List<Profile> profiles)
        {
            Title = Properties.Resources.BackupProfilesTitle;
            Width = 360;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 30));
            Foreground = Brushes.White;

            var grid = new StackPanel { Margin = new Thickness(20) };

            var label = new TextBlock
            {
                Text = Properties.Resources.SelectProfilesToBackup,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 14
            };
            grid.Children.Add(label);

            var checkboxesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

            foreach (var p in profiles)
            {
                var cb = new CheckBox
                {
                    Content = p.DisplayName,
                    Tag = p.Id,
                    IsChecked = true,
                    Margin = new Thickness(0, 4, 0, 4),
                    Foreground = Brushes.White
                };
                checkboxesPanel.Children.Add(cb);
            }
            grid.Children.Add(checkboxesPanel);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnCancel = new Button
            {
                Content = Properties.Resources.Cancel,
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Content = Properties.Resources.BackupData,
                Width = 100,
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            btnOk.Click += (s, e) =>
            {
                SelectedProfileIds = checkboxesPanel.Children.OfType<CheckBox>()
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Tag as string)
                    .Where(id => id != null)
                    .ToList()!;
                DialogResult = true;
                Close();
            };
            btnPanel.Children.Add(btnOk);

            grid.Children.Add(btnPanel);
            Content = grid;
        }
    }
}
