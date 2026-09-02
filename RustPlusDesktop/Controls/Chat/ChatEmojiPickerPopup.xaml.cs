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
        RefreshContent();
    }

    private void RefreshContent()
    {
        if (TxtSearch == null || EmojiList == null || EmptyNotice == null)
            return;

        var query = TxtSearch.Text?.Trim() ?? string.Empty;

        // One unified list: the custom emojis by default, and the whole emoji + Rust item catalog
        // once something is typed. No separate categories to switch between.
        var items = string.IsNullOrEmpty(query)
            ? EmojiService.CustomEmojis.ToList()
            : EmojiService.Search(query, 60).ToList();

        EmojiList.ItemsSource = items;
        EmptyNotice.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshContent();
    }

    private void ItemRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EmojiEntry entry)
        {
            EmojiSelected?.Invoke(entry);
        }
    }
}
