using RustPlusDesk.Services;
using System.Windows;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _lfgWired;

    /// <summary>
    /// Opens the Looking for Group panel from the Team tab. It is a panel rather than a window so
    /// it sits where the team already is — you are looking for people to play with while looking
    /// at the people you play with.
    /// </summary>
    private void BtnLfg_Click(object sender, RoutedEventArgs e)
    {
        if (LfgPanel.Visibility == Visibility.Visible)
        {
            LfgPanel.Visibility = Visibility.Collapsed;
            return;
        }

        if (!_lfgWired)
        {
            LfgPanel.CloseRequested += (_, __) => LfgPanel.Visibility = Visibility.Collapsed;
            LfgPanel.CloudSetupRequested += (_, __) =>
            {
                // Same dialog the cloud icon under the device list opens, so there is one place
                // that explains cloud rather than two that drift apart.
                LfgPanel.Visibility = Visibility.Collapsed;
                new Views.Windows.CloudAccountWindow(this).ShowDialog();
            };
            _lfgWired = true;
        }

        LfgPanel.Refresh();
        LfgPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Opens the Community area from the rail. Same panel as the Team tab's LFG button for now —
    /// global chat arrives here next, and the two share one inbox, so they share one surface
    /// rather than two that each hold half the messages.
    /// </summary>
    private void BtnSocial_Click(object sender, RoutedEventArgs e) => BtnLfg_Click(sender, e);
}
