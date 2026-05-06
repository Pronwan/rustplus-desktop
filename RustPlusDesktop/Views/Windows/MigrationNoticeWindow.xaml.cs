using System.Windows;

namespace RustPlusDesk.Views;

public partial class MigrationNoticeWindow : Window
{
    public bool ShouldReset { get; private set; } = false;

    public MigrationNoticeWindow()
    {
        InitializeComponent();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ShouldReset = true;
        Close();
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        ShouldReset = false;
        Close();
    }

    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }
}
