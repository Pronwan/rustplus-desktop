using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Models;

namespace RustPlusDesk.Views
{
    public enum RestoreMode
    {
        Append,
        Replace
    }

    public class RestoreProfileDialog : Window
    {
        public List<string> SelectedProfileIds { get; private set; } = new();
        public RestoreMode Mode { get; private set; } = RestoreMode.Append;

        public RestoreProfileDialog(List<Profile> profilesInBackup, bool hasExistingProfiles)
        {
            Title = Properties.Resources.RestoreProfilesTitle;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 30));
            Foreground = Brushes.White;

            var grid = new StackPanel { Margin = new Thickness(20) };

            var label = new TextBlock
            {
                Text = Properties.Resources.SelectProfilesToRestore,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 14
            };
            grid.Children.Add(label);

            var checkboxesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

            foreach (var p in profilesInBackup)
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

            if (hasExistingProfiles)
            {
                var modeLabel = new TextBlock
                {
                    Text = Properties.Resources.RestoreMode,
                    Margin = new Thickness(0, 0, 0, 8),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold
                };
                grid.Children.Add(modeLabel);

                var modePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

                var rbAppend = new RadioButton
                {
                    Content = Properties.Resources.RestoreModeAppend,
                    IsChecked = true,
                    Tag = RestoreMode.Append,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                modePanel.Children.Add(rbAppend);

                var rbReplace = new RadioButton
                {
                    Content = Properties.Resources.RestoreModeReplace,
                    Tag = RestoreMode.Replace,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                modePanel.Children.Add(rbReplace);

                grid.Children.Add(modePanel);
            }

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
                Content = Properties.Resources.RestoreData,
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

                if (hasExistingProfiles)
                {
                    var rbAppend = grid.Children.OfType<StackPanel>()
                        .SelectMany(sp => sp.Children.OfType<RadioButton>())
                        .FirstOrDefault(rb => rb.IsChecked == true);
                    if (rbAppend != null)
                        Mode = (RestoreMode)rbAppend.Tag!;
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
