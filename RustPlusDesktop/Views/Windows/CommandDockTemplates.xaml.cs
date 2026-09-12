using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace RustPlusDesk
{
    /// <summary>
    /// Named arrangements of the dock.
    ///
    /// Hovering a name outlines that arrangement over the dock itself rather than drawing a
    /// thumbnail here: the outline is at the real size in the real place, which is the only
    /// preview that answers "will this fit beside my game".
    /// </summary>
    public partial class CommandDockTemplates : Window
    {
        public MiniMapWindow? Dock { get; set; }

        public CommandDockTemplates()
        {
            InitializeComponent();
            Closed += (_, __) => Dock?.HidePresetPreview();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void TxtName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            SaveCurrent();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveCurrent();

        private string? _renaming;

        private void SaveCurrent()
        {
            var name = TxtName.Text?.Trim();
            if (string.IsNullOrEmpty(name) || Dock == null) return;

            // The same box does both. Renaming puts the old name in it, so pressing save then
            // means "call it this" rather than "store the dock again under a second name".
            if (_renaming != null)
            {
                Dock.RenamePreset(_renaming, name);
                _renaming = null;
            }
            else
            {
                Dock.SavePreset(name);
            }

            TxtName.Text = "";
            Refresh();
        }

        public void Refresh()
        {
            PresetList.Children.Clear();

            var presets = Dock?.Presets ?? Array.Empty<CommandDockPreset>();
            if (presets.Count == 0)
            {
                PresetList.Children.Add(new TextBlock
                {
                    Text = Loc.Text("CommandDockTemplatesEmpty",
                        "Nothing saved yet. Arrange the dock, give it a name and save it."),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 2, 4, 6),
                    Foreground = TryFindResource("TextSubtle") as Brush ?? Brushes.Gray,
                });
                return;
            }

            foreach (var preset in presets.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                PresetList.Children.Add(BuildRow(preset));
        }

        private UIElement BuildRow(CommandDockPreset preset)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 8, 8, 8),
                Margin = new Thickness(0, 0, 0, 5),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1E, 0x27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x2E, 0x3D)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Child = grid;

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = preset.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush ?? Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = string.Format(
                    Loc.Text("CommandDockTemplateSummary", "{0} tiles · saved {1}"),
                    preset.Tiles.Count,
                    preset.SavedUtc.ToLocalTime().ToString("d MMM, HH:mm")),
                FontSize = 10,
                Foreground = TryFindResource("TextSubtle") as Brush ?? Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });
            grid.Children.Add(text);

            var rename = SmallButton(SymbolRegular.Edit24, Loc.Text("CommandDockTemplateRename", "Rename"));
            Grid.SetColumn(rename, 1);
            grid.Children.Add(rename);

            var delete = SmallButton(SymbolRegular.Delete24, Loc.Text("CommandDockTemplateDelete", "Delete"), isDanger: true);
            Grid.SetColumn(delete, 2);
            grid.Children.Add(delete);

            // The whole row loads; the two buttons stop the press before it gets that far.
            row.MouseLeftButtonUp += (_, __) => { Dock?.ApplyPreset(preset.Id); Close(); };

            row.MouseEnter += (_, __) =>
            {
                row.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x37));
                row.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x47, 0x5C));
                Dock?.ShowPresetPreview(preset.Id);
            };
            row.MouseLeave += (_, __) =>
            {
                row.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1E, 0x27));
                row.BorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x2E, 0x3D));
                Dock?.HidePresetPreview();
            };

            rename.MouseLeftButtonUp += (_, e) => { e.Handled = true; BeginRename(preset); };
            delete.MouseLeftButtonUp += (_, e) => { e.Handled = true; ConfirmDelete(preset); };

            return row;
        }

        private void BeginRename(CommandDockPreset preset)
        {
            TxtName.Text = preset.Name;
            TxtName.Focus();
            TxtName.SelectAll();
            _renaming = preset.Id;
        }

        private async void ConfirmDelete(CommandDockPreset preset)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.Text("CommandDockTemplateDelete", "Delete"),
                Content = string.Format(
                    Loc.Text("CommandDockTemplateDeleteConfirm", "Delete the arrangement “{0}”?"),
                    preset.Name),
                PrimaryButtonText = Loc.Text("CommandDockTemplateDelete", "Delete"),
                CloseButtonText = Loc.Text("Cancel", "Cancel"),
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

            Dock?.DeletePreset(preset.Id);
            Dock?.HidePresetPreview();
            Refresh();
        }

        private Border SmallButton(SymbolRegular symbol, string tooltip, bool isDanger = false)
        {
            var button = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x27, 0x34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x37, 0x48)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = new SymbolIcon
                {
                    Symbol = symbol,
                    FontSize = 13,
                    Foreground = isDanger
                        ? new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71))
                        : (TryFindResource("TextSubtle") as Brush ?? Brushes.Gray),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            button.MouseEnter += (_, __) =>
            {
                button.Background = new SolidColorBrush(Color.FromRgb(0x29, 0x34, 0x44));
                button.BorderBrush = isDanger
                    ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
            };
            button.MouseLeave += (_, __) =>
            {
                button.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x27, 0x34));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x37, 0x48));
            };

            ToolTipService.SetToolTip(button, tooltip);
            return button;
        }
    }
}
