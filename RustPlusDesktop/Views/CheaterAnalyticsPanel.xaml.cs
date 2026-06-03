using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesk.Views
{
    public partial class CheaterAnalyticsPanel : UserControl
    {
        public CheaterAnalyticsPanel()
        {
            InitializeComponent();
        }

        private void BtnClosePanel_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
        }
    }
}
