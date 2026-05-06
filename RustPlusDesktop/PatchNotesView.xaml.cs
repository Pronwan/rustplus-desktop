using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace RustPlusDesk.Views;

/// <summary>
/// Reusable patch-notes body. Hosted today by the slide-in PatchNotesPanel
/// in MainWindow; previously lived inside PatchNotesWindow as a standalone
/// popup. Centralised here so future version-history edits only touch one
/// XAML file.
/// </summary>
public partial class PatchNotesView : UserControl
{
    public PatchNotesView()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch { /* ignore — best-effort browser open */ }
    }
}
