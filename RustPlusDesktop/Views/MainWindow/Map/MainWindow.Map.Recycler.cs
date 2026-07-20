using System.Windows;

namespace RustPlusDesk.Views
{
    public partial class MainWindow
    {
        private void RecyclerCalculator_CloseRequested(object sender, RoutedEventArgs e) =>
            ReturnToLastWorkspace();
    }
}
