using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

public partial class ChatCommandsOverlay : UserControl
{
    public ChatCommandsOverlay()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler CloseRequested;
    public event RoutedEventHandler? CommandsEnabledChanged;

    public void SetMasterBlocked(bool blocked, string message)
    {
        if (EnableChatCommandsCheckBox != null)
            EnableChatCommandsCheckBox.IsEnabled = !blocked;

        if (ChatCommandsMasterWarning != null)
            ChatCommandsMasterWarning.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;

        if (ChatCommandsMasterWarningText != null)
            ChatCommandsMasterWarningText.Text = message;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, e);
    }

    private void EnableChatCommandsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        CommandsEnabledChanged?.Invoke(this, e);
    }
}
