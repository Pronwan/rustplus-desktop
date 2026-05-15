using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views;

public partial class ChatCommandsOverlay : UserControl
{
    public ChatCommandsOverlay()
    {
        InitializeComponent();
        RustPlusDesk.Services.ChineseLocalizationService.ApplyTo(this);
    }

    public event RoutedEventHandler CloseRequested;

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, e);
    }
}
