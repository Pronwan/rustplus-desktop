using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Services.Emoji;

namespace RustPlusDesk.Controls.Chat;

public partial class ChatEmojiAutocompletePopup : UserControl
{
    public event Action<EmojiEntry>? ItemSelected;

    private readonly List<EmojiEntry> _currentItems = new();
    private int _selectedIndex = -1;

    public ChatEmojiAutocompletePopup()
    {
        InitializeComponent();
    }

    public int ItemCount => _currentItems.Count;

    public void SetSuggestions(IEnumerable<EmojiEntry> items)
    {
        _currentItems.Clear();
        _currentItems.AddRange(items);

        if (ItemsList != null)
        {
            ItemsList.ItemsSource = null;
            ItemsList.ItemsSource = _currentItems;
        }

        _selectedIndex = _currentItems.Count > 0 ? 0 : -1;
        UpdateSelectionVisuals();
    }

    public bool SelectNext()
    {
        if (_currentItems.Count == 0) return false;
        _selectedIndex = (_selectedIndex + 1) % _currentItems.Count;
        UpdateSelectionVisuals();
        return true;
    }

    public bool SelectPrevious()
    {
        if (_currentItems.Count == 0) return false;
        _selectedIndex = (_selectedIndex - 1 + _currentItems.Count) % _currentItems.Count;
        UpdateSelectionVisuals();
        return true;
    }

    public EmojiEntry? GetSelectedEntry()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _currentItems.Count)
        {
            return _currentItems[_selectedIndex];
        }
        return null;
    }

    private void UpdateSelectionVisuals()
    {
        if (ItemsList == null) return;
        for (int i = 0; i < ItemsList.Items.Count; i++)
        {
            var container = ItemsList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container != null)
            {
                var border = FindVisualChild<Border>(container);
                if (border != null)
                {
                    border.Background = (i == _selectedIndex)
                        ? new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF))
                        : Brushes.Transparent;
                }
            }
        }
    }

    private void ItemRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EmojiEntry entry)
        {
            ItemSelected?.Invoke(entry);
        }
    }

    private void ItemRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EmojiEntry entry)
        {
            _selectedIndex = _currentItems.IndexOf(entry);
            UpdateSelectionVisuals();
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var sub = FindVisualChild<T>(child);
            if (sub != null) return sub;
        }
        return null;
    }
}
