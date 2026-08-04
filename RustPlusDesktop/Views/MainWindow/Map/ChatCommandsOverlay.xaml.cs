using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class ChatCommandsOverlay : UserControl
{
    public ChatCommandsOverlay()
    {
        InitializeComponent();

        // Ask the central capability service rather than being told from outside. The overlay
        // is created and shown independently of the connect flow, so pushing state into it
        // would mean remembering to do so from every call site.
        Loaded += (_, __) => ApplyEventCapabilities();
        EventCapabilities.Changed += OnCapabilitiesChanged;
        Unloaded += (_, __) => EventCapabilities.Changed -= OnCapabilitiesChanged;
    }

    private void OnCapabilitiesChanged() => Dispatcher.Invoke(ApplyEventCapabilities);

    /// <summary>
    /// Patrol Heli and Travelling Vendor have no server-wide audio cue, so on a server without
    /// event markers those commands can never answer. Hiding the rows is honest; leaving them
    /// configurable would invite players to set up a command that always replies "unknown".
    /// </summary>
    private void ApplyEventCapabilities()
    {
        var vis = EventCapabilities.IsCloudSourced ? Visibility.Collapsed : Visibility.Visible;

        foreach (var element in new UIElement?[]
                 {
                     CmdRowHeliLabel, CmdRowHeliPrefix, CmdRowHeliBox,
                     CmdRowVendorLabel, CmdRowVendorPrefix, CmdRowVendorBox,
                 })
        {
            if (element != null) element.Visibility = vis;
        }
    }

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

    private void EnableChatCommandsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        CommandsEnabledChanged?.Invoke(this, e);
    }
}
