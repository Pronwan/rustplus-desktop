using System;
using System.Windows;

namespace RustPlusDesk.Views
{
    public partial class MainWindow
    {
        private void BtnOpenRecycler_Click(object sender, RoutedEventArgs e)
        {
            if (RecyclerOverlayPanel == null) return;

            if (RecyclerOverlayPanel.Visibility == Visibility.Visible)
            {
                RecyclerOverlayPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Collapse other overlays that might get in the way
                if (ShopSearchContent != null)
                {
                    ShopSearchContent.Visibility = Visibility.Collapsed;
                }
                if (ChatContentBorder != null)
                {
                    ChatContentBorder.Visibility = Visibility.Collapsed;
                }
                if (ChatOverlayPanel != null)
                {
                    ChatOverlayPanel.Visibility = Visibility.Visible;
                }
                
                RecyclerOverlayPanel.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseRecycler_Click(object sender, RoutedEventArgs e)
        {
            if (RecyclerOverlayPanel != null)
            {
                RecyclerOverlayPanel.Visibility = Visibility.Collapsed;
            }
            if (ChatOverlayPanel != null)
            {
                ChatOverlayPanel.Visibility = Visibility.Visible;
            }
        }
    }
}
