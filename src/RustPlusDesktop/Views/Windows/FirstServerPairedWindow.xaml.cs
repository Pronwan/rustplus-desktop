using System.Windows;

namespace RustPlusDesk.Views.Windows;

/// <summary>
/// Shown when the first server lands in an empty list: it worked, the server is selected, and
/// one click connects. Picking it from the dropdown and then finding Connect was where new
/// users got stuck.
/// </summary>
public partial class FirstServerPairedWindow : Wpf.Ui.Controls.FluentWindow
{
    public bool ConnectNow { get; private set; }

    public FirstServerPairedWindow(Window owner, string serverName)
    {
        InitializeComponent();
        Owner = owner;
        TxtServerName.Text = serverName;
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e) => Finish(true);

    private void BtnLater_Click(object sender, RoutedEventArgs e) => Finish(false);

    private void Finish(bool connectNow)
    {
        ConnectNow = connectNow;
        DialogResult = true;
        Close();
    }
}
