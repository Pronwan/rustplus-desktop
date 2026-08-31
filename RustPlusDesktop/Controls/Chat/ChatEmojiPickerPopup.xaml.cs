using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.Services.Emoji;

namespace RustPlusDesk.Controls.Chat;

public partial class ChatEmojiPickerPopup : UserControl
{
    public event Action<EmojiEntry>? EmojiSelected;

    public ChatEmojiPickerPopup()
    {
        InitializeComponent();
        Loaded += ChatEmojiPickerPopup_Loaded;
    }

    private void ChatEmojiPickerPopup_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshContent();
    }

    public void Reset()
    {
        TxtSearch.Text = string.Empty;
        TabEmojis.IsChecked = true;
        RefreshContent();
    }

    private void RefreshContent()
    {
        if (TxtSearch == null || EmojiGrid == null || ItemList == null || TabEmojis == null)
            return;

        var query = TxtSearch.Text?.Trim() ?? string.Empty;

        if (!string.IsNullOrEmpty(query))
        {
            // Searching across all emojis & items
            EmojiGrid.Visibility = Visibility.Collapsed;
            ItemList.Visibility = Visibility.Visible;
            ItemList.ItemsSource = EmojiService.Search(query, 50).ToList();
        }
        else if (TabEmojis.IsChecked == true)
        {
            EmojiGrid.Visibility = Visibility.Visible;
            ItemList.Visibility = Visibility.Collapsed;
            EmojiGrid.ItemsSource = EmojiService.CustomEmojis;
        }
        else
        {
            EmojiGrid.Visibility = Visibility.Collapsed;
            ItemList.Visibility = Visibility.Visible;
            ItemList.ItemsSource = EmojiService.Search(string.Empty, 50).ToList();
        }
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshContent();
    }

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        RefreshContent();
    }

    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EmojiEntry entry)
        {
            EmojiSelected?.Invoke(entry);
        }
    }

    private void ItemRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EmojiEntry entry)
        {
            EmojiSelected?.Invoke(entry);
        }
    }
}
