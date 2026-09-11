using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    /// <summary>
    /// The dock's appearance defaults, in the main settings.
    ///
    /// These are not a second set of switches for what a tile's own gear controls — they are what
    /// a tile uses until that gear is touched. Two controls over one value end up disagreeing,
    /// and then nobody can tell which of them is in charge.
    /// </summary>
    public partial class AppSettingsOverlay : UserControl
    {
        private const string DockCacheKey = "minimap_dock";
        private bool _loadingDockDefaults;

        private void LoadCommandDockDefaults()
        {
            var dock = StorageService.LoadCache<CommandDockLayout>(DockCacheKey) ?? new CommandDockLayout();

            _loadingDockDefaults = true;
            try
            {
                SliderDockOpacity.Value = Math.Clamp(dock.DefaultOpacity, 0, 1);
                SliderDockFontScale.Value = Math.Clamp(dock.DefaultFontScale, 0.7, 2.0);
            }
            finally
            {
                _loadingDockDefaults = false;
            }

            BuildDockColorSwatches(dock.DefaultTextColorKey ?? CommandDockTextColors.Auto);
        }

        private void OnCommandDockDefaultChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // RangeBase raises this while the XAML is still being parsed, because a Value set in
            // markup is a change from the default. At that moment the second slider has not been
            // created yet, and reading it throws inside the constructor — which surfaces as a
            // TargetInvocationException from the BAML loader rather than anywhere near here.
            // _isSettingsInitialized is the same guard every other handler in this file uses.
            if (!_isSettingsInitialized || _loadingDockDefaults) return;
            if (SliderDockOpacity == null || SliderDockFontScale == null) return;

            UpdateDock(dock =>
            {
                dock.DefaultOpacity = SliderDockOpacity.Value;
                dock.DefaultFontScale = SliderDockFontScale.Value;
            });
        }

        private void BuildDockColorSwatches(string current)
        {
            DockColorSwatches.Children.Clear();

            foreach (var key in CommandDockTextColors.All)
            {
                bool selected = key == current;

                var swatch = new Border
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(0, 0, 5, 0),
                    CornerRadius = new CornerRadius(5),
                    Background = TryFindResource("SurfaceAlt") as Brush ?? Brushes.Transparent,
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    BorderBrush = (selected
                        ? TryFindResource("Accent")
                        : TryFindResource("CardBorder")) as Brush ?? Brushes.Gray,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        // The letter in the colour itself, so the swatch shows what the text
                        // will look like rather than only which hue was picked.
                        Text = key == CommandDockTextColors.Auto ? "—" : "A",
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        Foreground = DockSwatchBrush(key),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };

                ToolTipService.SetToolTip(swatch, DockColorLabel(key));

                var chosen = key;
                swatch.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    UpdateDock(dock => dock.DefaultTextColorKey = chosen);
                    BuildDockColorSwatches(chosen);
                };

                DockColorSwatches.Children.Add(swatch);
            }
        }

        private Brush DockSwatchBrush(string key) => key switch
        {
            CommandDockTextColors.White => Brushes.White,
            CommandDockTextColors.Black => new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x14)),
            CommandDockTextColors.Cyan => new SolidColorBrush(Color.FromRgb(0x3F, 0xD7, 0xFF)),
            CommandDockTextColors.Amber => new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x46)),
            CommandDockTextColors.Red => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E)),
            CommandDockTextColors.Green => new SolidColorBrush(Color.FromRgb(0x5B, 0xD9, 0x8A)),
            _ => TryFindResource("TextPrimary") as Brush ?? Brushes.White,
        };

        private static string DockColorLabel(string key) => key switch
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

        private async void BtnResetCommandDock_Click(object sender, RoutedEventArgs e)
        {
            var confirm = new WpfUi.MessageBox
            {
                Title = Loc.Text("CommandDockReset", "Reset the dock"),
                Content = Loc.Text("CommandDockResetConfirm",
                    "Every tile, its position and its settings are removed. The mini-map itself is not affected."),
                PrimaryButtonText = Loc.Text("CommandDockReset", "Reset the dock"),
                CloseButtonText = Loc.Text("Cancel", "Cancel"),
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (await confirm.ShowDialogAsync() != WpfUi.MessageBoxResult.Primary) return;

            // A fresh layout rather than an emptied one: position, defaults and the map's
            // removed flag all go back to where a new install starts.
            StorageService.SaveCache(DockCacheKey, new CommandDockLayout());
            LoadCommandDockDefaults();
            NotifyDockChanged();
        }

        /// <summary>
        /// Reads, changes and writes the layout in one go.
        ///
        /// Deliberately re-read each time instead of held in a field: the mini-map owns this file
        /// while it is open and writes tile moves into it, so a cached copy here would put a
        /// dragged tile back where it was the moment a slider moved.
        /// </summary>
        private void UpdateDock(Action<CommandDockLayout> change)
        {
            var dock = StorageService.LoadCache<CommandDockLayout>(DockCacheKey) ?? new CommandDockLayout();
            change(dock);
            StorageService.SaveCache(DockCacheKey, dock);
            NotifyDockChanged();
        }

        private static void NotifyDockChanged()
        {
            var dock = Application.Current?.Windows.OfType<MiniMapWindow>().FirstOrDefault();
            dock?.ReloadDockLayout();
        }
    }
}
