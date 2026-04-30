using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _mapReady;

    private void SetupMapScene(BitmapSource bmp)
    {
        double wDip = bmp.PixelWidth * (96.0 / bmp.DpiX);
        double hDip = bmp.PixelHeight * (96.0 / bmp.DpiY);

        const double padPx = 000; // Zeichen-Rand

        ImgMap.Stretch = Stretch.None;
        ImgMap.HorizontalAlignment = HorizontalAlignment.Left;
        ImgMap.VerticalAlignment = VerticalAlignment.Top;
        ImgMap.Width = wDip;
        ImgMap.Height = hDip;

        GridLayer.Width = wDip;
        GridLayer.Height = hDip;
        GridLayer.IsHitTestVisible = false;

        // WICHTIG: Overlay groesser machen, aber Map nicht anfassen
        Overlay.Width = wDip + padPx * 2;
        Overlay.Height = hDip + padPx * 2;
        Overlay.IsHitTestVisible = true;
        Overlay.Background = Brushes.Transparent;
        EnsureShopsHoverPopup();

        _scene ??= new Grid();
        _scene.Width = wDip + padPx * 2;
        _scene.Height = hDip + padPx * 2;

        (ImgMap.Parent as Panel)?.Children.Remove(ImgMap);
        (GridLayer.Parent as Panel)?.Children.Remove(GridLayer);
        (Overlay.Parent as Panel)?.Children.Remove(Overlay);

        _scene.Children.Clear();

        // Map bei (padPx, padPx)? -> NEIN, jetzt bei (0,0)!
        _scene.Children.Add(ImgMap); Panel.SetZIndex(ImgMap, 0);
        _scene.Children.Add(GridLayer); Panel.SetZIndex(GridLayer, 1);
        _scene.Children.Add(Overlay); Panel.SetZIndex(Overlay, 2);

        _scene.RenderTransform = MapTransform;

        if (_mapView == null)
        {
            _mapView = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
            WebViewHost.Children.Add(_mapView);
            Panel.SetZIndex(_mapView, 0);
        }
        _mapView.Child = _scene;
    }

    private void ResetMapDisplay()
    {
        _mapBaseBmp = null;
        _mapReady = false;

        ImgMap.Source = null;
        GridLayer.Children.Clear();

        if (MapPlaceholder != null) MapPlaceholder.Visibility = Visibility.Visible;
        if (_mapView != null) _mapView.Visibility = Visibility.Collapsed;

        if (_miniMap != null)
        {
            _miniMap.Close();
            _miniMap = null;
            _miniMapBrush = null;
        }
    }

    private void ShowMapBasic(BitmapSource bmp)
    {
        if (_webView != null) _webView.Visibility = Visibility.Collapsed;

        _mapBaseBmp = bmp;
        _mapReady = true;
        if (MapPlaceholder != null) MapPlaceholder.Visibility = Visibility.Collapsed;
        if (_mapView != null) _mapView.Visibility = Visibility.Visible;
        _staticMarkers.Clear();            // << keine Testpunkte

        ImgMap.Source = bmp;               // zunaechst nackte Map
        SetupMapScene(bmp);
        RedrawGrid();
    }
}
