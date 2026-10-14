using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;

namespace RustPlusDesk.Views
{
    /// <summary>
    /// What the dock itself can be set to, as opposed to what the map can.
    ///
    /// The gear in the title bar used to open the mini-map's panel — shape, size, layers — on a
    /// window that need not have a map on it at all, and whose tiles none of those settings
    /// reached. The map keeps that panel, opened from its own gear. This is the dock's.
    /// </summary>
    public partial class CommandDockSettingsOverlay : UserControl
    {
        /// <summary>
        /// Set while the panel is being filled in, and true until it first is.
        ///
        /// Starting true is the half that matters. Setting a slider's Value in the markup
        /// raises ValueChanged during InitializeComponent, in declaration order — so the
        /// first slider's handler runs while the label underneath it is still null, and read
        /// it. Nothing a handler does before the panel has been loaded is a user's doing.
        /// </summary>
        private bool _loading = true;

        public MiniMapWindow? ParentWindow { get; set; }

        public CommandDockSettingsOverlay()
        {
            InitializeComponent();
        }

        /// <summary>Fills the panel from the dock it belongs to. Called every time it opens.</summary>
        public void Load(CommandDockLayout dock, string? currentMonitor)
        {
            _loading = true;

            try
            {
                CmbMonitor.Items.Clear();

                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    CmbMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = screen.DeviceName.Replace(@"\\.\", "") +
                                  $" ({screen.Bounds.Width}×{screen.Bounds.Height})",
                        Tag = screen.DeviceName,
                    });
                }

                foreach (ComboBoxItem item in CmbMonitor.Items)
                    if ((string)item.Tag == currentMonitor) CmbMonitor.SelectedItem = item;

                ChkGlobalOpacity.IsChecked = dock.OverrideOpacity;
                ChkGlobalFont.IsChecked = dock.OverrideFontScale;
                ChkGlobalColor.IsChecked = dock.OverrideTextColor;

                SliOpacity.Value = Math.Clamp(dock.DefaultOpacity, 0, 1);
                SliFont.Value = Math.Clamp(dock.DefaultFontScale, 0.7, 2.0);

                BuildSwatches(dock.DefaultTextColorKey ?? CommandDockTextColors.Auto);
                ShowValues();
                ApplyEnabled();
            }
            finally
            {
                _loading = false;
            }
        }

        private void ShowValues()
        {
            // Belt as well as braces: these run from handlers that fire while the markup is
            // still being built, and the control a line below has not been made yet.
            if (LblOpacity == null || LblFont == null) return;

            LblOpacity.Text = string.Format(
                Loc.Text("CommandDockTileOpacity", "Opacity {0}%"), (int)Math.Round(SliOpacity.Value * 100));

            LblFont.Text = string.Format(
                Loc.Text("CommandDockTileFontSize", "Text size {0}%"), (int)Math.Round(SliFont.Value * 100));
        }

        /// <summary>
        /// Greys out a value whose switch is off.
        ///
        /// It stays readable rather than disappearing: the number is what the tiles would get if
        /// the switch were turned on, and hiding it would make the switch a guess.
        /// </summary>
        private void ApplyEnabled()
        {
            if (SliOpacity == null || SliFont == null || ColorSwatches == null) return;

            SliOpacity.IsEnabled = ChkGlobalOpacity.IsChecked == true;
            SliFont.IsEnabled = ChkGlobalFont.IsChecked == true;
            ColorSwatches.IsEnabled = ChkGlobalColor.IsChecked == true;

            SliOpacity.Opacity = SliOpacity.IsEnabled ? 1 : 0.45;
            SliFont.Opacity = SliFont.IsEnabled ? 1 : 0.45;
            ColorSwatches.Opacity = ColorSwatches.IsEnabled ? 1 : 0.45;
        }

        private void Override_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            ApplyEnabled();

            ParentWindow?.UpdateDockAppearance(dock =>
            {
                dock.OverrideOpacity = ChkGlobalOpacity.IsChecked == true;
                dock.OverrideFontScale = ChkGlobalFont.IsChecked == true;
                dock.OverrideTextColor = ChkGlobalColor.IsChecked == true;
            });
        }

        private void Value_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;

            ShowValues();

            ParentWindow?.UpdateDockAppearance(dock =>
            {
                dock.DefaultOpacity = SliOpacity.Value;
                dock.DefaultFontScale = SliFont.Value;
            });
        }

        private void CmbMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ParentWindow == null) return;

            if (CmbMonitor.SelectedItem is ComboBoxItem { Tag: string deviceName })
                ParentWindow.MoveToMonitor(deviceName);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => ParentWindow?.CloseDockSettings();

        /// <summary>
        /// Puts the look back to how it ships: no global overrides, the defaults at their
        /// starting values, and every tile's own appearance cleared.
        ///
        /// The arrangement is deliberately untouched. Somebody asking to reset how the dock
        /// looks has not asked to lose where everything is.
        /// </summary>
        private async void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.Text("CommandDockResetLook", "Reset the look"),
                Content = Loc.Text("CommandDockResetLookConfirm",
                    "Clear the global settings and every tile's own opacity, text size and colour? " +
                    "The tiles themselves stay where they are."),
                PrimaryButtonText = Loc.Text("CommandDockResetLook", "Reset the look"),
                CloseButtonText = Loc.Text("Cancel", "Cancel"),
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

            ParentWindow?.UpdateDockAppearance(dock =>
            {
                dock.OverrideOpacity = false;
                dock.OverrideFontScale = false;
                dock.OverrideTextColor = false;

                dock.DefaultOpacity = 1.0;
                dock.DefaultFontScale = 1.0;
                dock.DefaultTextColorKey = CommandDockTextColors.Auto;

                foreach (var tile in dock.Tiles)
                {
                    tile.Opacity = null;
                    tile.FontScale = null;
                    tile.TextColorKey = null;
                }
            });

            ParentWindow?.ReloadDockSettingsPanel();
        }

        /// <summary>The colours a tile's text can be set to, as a row of letters in each.</summary>
        private void BuildSwatches(string current)
        {
            ColorSwatches.Children.Clear();

            foreach (var key in CommandDockTextColors.All)
            {
                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 4, 4),
                    CornerRadius = new CornerRadius(4),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(key == current ? 2 : 1),
                    BorderBrush = key == current
                        ? new SolidColorBrush(Color.FromRgb(0x3F, 0xD7, 0xFF))
                        : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                    Child = new TextBlock
                    {
                        Text = key == CommandDockTextColors.Auto ? "—" : "A",
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = SwatchBrush(key),
                    },
                };

                ToolTipService.SetToolTip(swatch, ColorLabel(key));

                var chosen = key;
                swatch.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (ColorSwatches.IsEnabled != true) return;

                    ParentWindow?.UpdateDockAppearance(dock => dock.DefaultTextColorKey = chosen);
                    BuildSwatches(chosen);
                };

                ColorSwatches.Children.Add(swatch);
            }
        }

        private static Brush SwatchBrush(string key) => key switch
        {
            CommandDockTextColors.White => Brushes.White,
            CommandDockTextColors.Black => Brushes.Black,
            CommandDockTextColors.Cyan => new SolidColorBrush(Color.FromRgb(0x3F, 0xD7, 0xFF)),
            CommandDockTextColors.Amber => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            CommandDockTextColors.Red => new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            CommandDockTextColors.Green => new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),
            _ => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
        };

        private static string ColorLabel(string key) => key switch
        {
            CommandDockTextColors.Auto => Loc.Text("CommandDockColorAuto", "Theme colour"),
            CommandDockTextColors.White => Loc.Text("CommandDockColorWhite", "White"),
            CommandDockTextColors.Black => Loc.Text("CommandDockColorBlack", "Black"),
            CommandDockTextColors.Cyan => Loc.Text("CommandDockColorCyan", "Cyan"),
            CommandDockTextColors.Amber => Loc.Text("CommandDockColorAmber", "Amber"),
            CommandDockTextColors.Red => Loc.Text("CommandDockColorRed", "Red"),
            CommandDockTextColors.Green => Loc.Text("CommandDockColorGreen", "Green"),
            _ => key,
        };
    }
}
