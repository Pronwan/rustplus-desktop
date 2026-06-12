using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.Models;

namespace RustPlusDesk.Views.Windows
{
    public partial class ChangeDeviceIconDialog : Window
    {
        public int? SelectedIconId { get; private set; }
        public string? SelectedIconShortName { get; private set; }
        public bool IsResetClicked { get; private set; }

        public ChangeDeviceIconDialog(int? currentIconId, string? currentIconShortName)
        {
            InitializeComponent();
            SelectedIconId = currentIconId;
            SelectedIconShortName = currentIconShortName;
            IsResetClicked = false;
            
            Loaded += (s, e) =>
            {
                TxtSearch.Focus();
            };
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
                    DialogResult = false;
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
            DialogResult = true;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveSelectionAndClose();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveSelectionAndClose()
        {
            if (LstItems.SelectedItem is ShopSearchControl.AutocompleteItem selected)
            {
                SelectedIconId = selected.Id;
                SelectedIconShortName = selected.ShortName;
            }
            DialogResult = true;
            Close();
        }
    }
}
