using System.Windows;
using RustPlusDesk.Helpers;

namespace RustPlusDesk.Views.Windows;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{VersionHelper.GetClientVersion()}";
    }

    public void SetStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(message));
            return;
        }
        StatusText.Text = message;
    }
}
