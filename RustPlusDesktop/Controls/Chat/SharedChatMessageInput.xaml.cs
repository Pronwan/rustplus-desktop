using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RustPlusDesk.Controls.Chat;

public partial class SharedChatMessageInput : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SharedChatMessageInput),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(SharedChatMessageInput),
        new PropertyMetadata("Type a message..."));

    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength),
        typeof(int),
        typeof(SharedChatMessageInput),
        new PropertyMetadata(500));

    public static readonly DependencyProperty SendButtonTextProperty = DependencyProperty.Register(
        nameof(SendButtonText),
        typeof(string),
        typeof(SharedChatMessageInput),
        new PropertyMetadata("Send"));

    public static readonly DependencyProperty ShowSendButtonProperty = DependencyProperty.Register(
        nameof(ShowSendButton),
        typeof(bool),
        typeof(SharedChatMessageInput),
        new PropertyMetadata(true));

    public static readonly DependencyProperty IsSendEnabledProperty = DependencyProperty.Register(
        nameof(IsSendEnabled),
        typeof(bool),
        typeof(SharedChatMessageInput),
        new PropertyMetadata(true));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public string SendButtonText
    {
        get => (string)GetValue(SendButtonTextProperty);
        set => SetValue(SendButtonTextProperty, value);
    }

    public bool ShowSendButton
    {
        get => (bool)GetValue(ShowSendButtonProperty);
        set => SetValue(ShowSendButtonProperty, value);
    }

    public bool IsSendEnabled
    {
        get => (bool)GetValue(IsSendEnabledProperty);
        set => SetValue(IsSendEnabledProperty, value);
    }

    public event Action<string>? SendRequested;
    public event KeyEventHandler? InputKeyDown;

    private readonly ChatEmojiInputHelper _emojiHelper;

    public SharedChatMessageInput()
    {
        InitializeComponent();

        _emojiHelper = new ChatEmojiInputHelper(
            InputTextBox,
            AutocompletePopup,
            AutocompleteControl,
            PickerPopup,
            PickerControl,
            BtnEmoji
        );
    }

    public TextBox InnerTextBox => InputTextBox;

    public void FocusInput()
    {
        InputTextBox.Focus();
    }

    public void Clear()
    {
        InputTextBox.Text = string.Empty;
    }

    private void BtnEmoji_Click(object sender, RoutedEventArgs e)
    {
        // Handled inside ChatEmojiInputHelper
    }

    private void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        TriggerSend();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        InputKeyDown?.Invoke(this, e);

        if (e.Key == Key.Enter && !AutocompletePopup.IsOpen)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                TriggerSend();
                e.Handled = true;
            }
        }
    }

    private void TriggerSend()
    {
        if (!IsSendEnabled) return;

        var message = InputTextBox.Text;
        if (!string.IsNullOrWhiteSpace(message))
        {
            SendRequested?.Invoke(message);
        }
    }
}
