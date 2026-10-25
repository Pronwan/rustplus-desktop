using System.Globalization;
using System.Windows;

namespace RustPlusDesk.Views.Windows;

/// <summary>
/// Asks, once, whether the 2D map should show the HD map read from the server's .map file
/// instead of the server's low-resolution image.
/// </summary>
public partial class MapHdImageWindow : Wpf.Ui.Controls.FluentWindow
{
    public bool UseRenderedImage { get; private set; }
    public bool Remember { get; private set; }

    /// <param name="fullResolutionExtraMb">What full resolution would add for this map, already rounded.</param>
    public MapHdImageWindow(Window owner, int fullResolutionExtraMb)
    {
        InitializeComponent();
        Owner = owner;

        string template = Properties.Resources.GetString("MapHdPromptRamNote");
        TxtRamNote.Text = string.Format(CultureInfo.CurrentCulture, template, fullResolutionExtraMb);
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e) => Finish(true);

    private void BtnNo_Click(object sender, RoutedEventArgs e) => Finish(false);

    private void Finish(bool useRenderedImage)
    {
        UseRenderedImage = useRenderedImage;
        Remember = ChkRemember.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
