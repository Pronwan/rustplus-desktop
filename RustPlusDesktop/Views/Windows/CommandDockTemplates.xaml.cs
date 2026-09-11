using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;

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
                Padding = new Thickness(10, 7, 6, 7),
                Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(6),
                Background = TryFindResource("SurfaceAlt") as Brush ?? Brushes.Transparent,
                Cursor = Cursors.Hand,
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
            });
            grid.Children.Add(text);

            var rename = SmallButton("\uE70F", Loc.Text("CommandDockTemplateRename", "Rename"));
            Grid.SetColumn(rename, 1);
            grid.Children.Add(rename);

            var delete = SmallButton("\uE74D", Loc.Text("CommandDockTemplateDelete", "Delete"));
            Grid.SetColumn(delete, 2);
            grid.Children.Add(delete);

            // The whole row loads; the two buttons stop the press before it gets that far.
            row.MouseLeftButtonUp += (_, __) => { Dock?.ApplyPreset(preset.Id); Close(); };

            row.MouseEnter += (_, __) => Dock?.ShowPresetPreview(preset.Id);
            row.MouseLeave += (_, __) => Dock?.HidePresetPreview();

            rename.MouseLeftButtonUp += (_, e) => { e.Handled = true; BeginRename(preset); };
            delete.MouseLeftButtonUp += (_, e) => { e.Handled = true; ConfirmDelete(preset); };

            return row;
        }

        private void BeginRename(CommandDockPreset preset)
        {
            // Reuses the name box rather than opening a dialog on top of a window that is itself
            // a dialog: it is already there, already focused by habit, and Enter already saves.
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

        private Border SmallButton(string glyph, string tooltip)
        {
            var button = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Background = TryFindResource("Surface") as Brush ?? Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            ToolTipService.SetToolTip(button, tooltip);
            return button;
        }
    }
}
