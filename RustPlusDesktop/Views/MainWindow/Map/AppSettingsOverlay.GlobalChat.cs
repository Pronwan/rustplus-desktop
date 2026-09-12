using System.Windows;

namespace RustPlusDesk.Views;

/// <summary>
/// The global chat strip's one setting.
///
/// It exists because the chevron on the strip hides it for good, and a control that turns something
/// off with no visible way to turn it back on is a trap rather than a preference.
/// </summary>
public partial class AppSettingsOverlay
{
    /// <summary>Puts the checkbox in step with the stored state. Called from LoadSettings.</summary>
    private void LoadGlobalChatSettings()
    {
        if (ChkGlobalChatTicker == null) return;
        ChkGlobalChatTicker.IsChecked = !Controls.Chat.GlobalChatTicker.IsHidden;
    }

    private void OnGlobalChatTickerToggled(object sender, RoutedEventArgs e)
    {
        if (!_isSettingsInitialized) return;
        if (ChkGlobalChatTicker == null) return;

        Controls.Chat.GlobalChatTicker.SetHidden(ChkGlobalChatTicker.IsChecked != true);

        // Applied now rather than on next launch: a setting that needs a restart to take effect
        // reads as one that did not work.
        ParentWindow?.RefreshGlobalChatTicker();
    }
}
