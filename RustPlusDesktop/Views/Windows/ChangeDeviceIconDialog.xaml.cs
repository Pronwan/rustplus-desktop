using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.Models;
using ShopSearchCtrl = RustPlusDesk.Views.ShopSearchControl;

namespace RustPlusDesk.Views.Windows
{
    public partial class ChangeDeviceIconDialog : Window
    {
        public int? SelectedIconId { get; private set; }
        public string? SelectedIconShortName { get; private set; }
        public bool IsResetClicked { get; private set; }
        public bool IsSaved { get; private set; }

        private readonly string _contextKey;

        public ChangeDeviceIconDialog(int? currentIconId, string? currentIconShortName, string contextKey = "Device")
        {
            InitializeComponent();
            SelectedIconId = currentIconId;
            SelectedIconShortName = currentIconShortName;
            IsResetClicked = false;
            IsSaved = false;
            _contextKey = string.IsNullOrWhiteSpace(contextKey) ? "Device" : contextKey;

            LoadLocalizedStrings();

            Loaded += (s, e) =>
            {
                TxtSearch.Focus();
                PreselectCurrentIcon(currentIconId);
            };
        }

        private static string GetString(string key, string fallback)
            => RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;

        private void LoadLocalizedStrings()
        {
            string contextLabel = GetString(_contextKey, _contextKey);
            string title = GetString($"Change{_contextKey}IconTitle", $"Change {contextLabel} Icon");
            string subtitle = GetString($"Change{_contextKey}IconSubtitle", $"Search and select an item icon for this {contextLabel.ToLowerInvariant()}.");

            TxtTitle.Text = title;
            TxtSubtitle.Text = subtitle;
            Title = title;

            TxtSearch.PlaceholderText = GetString("SearchIconsPlaceholder", "Search icons (e.g. turret, switch)...");
            BtnReset.Content = GetString("ResetToDefaultIcon", "Reset to Default Icon");
            BtnSave.Content = RustPlusDesk.Properties.Resources.Save;
            BtnCancel.Content = RustPlusDesk.Properties.Resources.Cancel;
        }

        private void PreselectCurrentIcon(int? currentIconId)
        {
            if (!currentIconId.HasValue)
                return;

            try
            {
                if (RustPlusDesk.Views.MainWindow.sItemsById.TryGetValue(currentIconId.Value, out var itemInfo))
                {
                    var currentItem = new ShopSearchCtrl.AutocompleteItem
                    {
                        Id = itemInfo.Id,
                        Display = itemInfo.Display,
                        ShortName = itemInfo.ShortName,
                        Icon = RustPlusDesk.Views.MainWindow.ResolveItemIcon(itemInfo.Id, itemInfo.ShortName, 32)
                    };

                    var list = new List<ShopSearchCtrl.AutocompleteItem> { currentItem };
                    LstItems.ItemsSource = list;
                    LstItems.SelectedItem = currentItem;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangeIconDialog] Preselect error: {ex.Message}");
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string query = TxtSearch.Text;
                var matches = RustPlusDesk.Views.MainWindow.SearchItems(query);
                LstItems.ItemsSource = matches;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChangeIconDialog] Search error: {ex.Message}");
            }
        }

        private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (LstItems.Items.Count > 0)
            {
                int index = LstItems.SelectedIndex;
                if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    index = (index + 1) % LstItems.Items.Count;
                    LstItems.SelectedIndex = index;
                    LstItems.ScrollIntoView(LstItems.SelectedItem);
                }
                else if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    index = index <= 0 ? LstItems.Items.Count - 1 : index - 1;
                    LstItems.SelectedIndex = index;
                    LstItems.ScrollIntoView(LstItems.SelectedItem);
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    if (LstItems.SelectedItem != null)
                    {
                        SaveSelectionAndClose();
                    }
                }
                else if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            }
        }

        private void LstItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstItems.SelectedItem is ShopSearchControl.AutocompleteItem selected)
            {
                SelectedIconId = selected.Id;
                SelectedIconShortName = selected.ShortName;
            }
        }

        private void LstItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstItems.SelectedItem != null)
            {
                SaveSelectionAndClose();
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            SelectedIconId = null;
            SelectedIconShortName = null;
            IsResetClicked = true;
            IsSaved = true;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectionAndClose();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SaveSelectionAndClose()
        {
            if (LstItems.SelectedItem is ShopSearchControl.AutocompleteItem selected)
            {
                SelectedIconId = selected.Id;
                SelectedIconShortName = selected.ShortName;
            }
            IsSaved = true;
            Close();
        }
    }
}
