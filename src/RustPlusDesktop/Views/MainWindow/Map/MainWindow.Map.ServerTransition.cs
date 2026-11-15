using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    internal static BitmapSource? CaptureServerMapTransition(FrameworkElement viewport)
    {
        if (viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0) return null;
        double scale = Math.Min(1, 1280 / Math.Max(viewport.ActualWidth, viewport.ActualHeight));
        int width = Math.Max(1, (int)Math.Round(viewport.ActualWidth * scale));
        int height = Math.Max(1, (int)Math.Round(viewport.ActualHeight * scale));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(viewport), null, new Rect(0, 0, width, height));
        var snapshot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        snapshot.Render(visual);
        snapshot.Freeze();
        return snapshot;
    }

    private void BeginServerMapTransition()
    {
        ServerMapTransitionImage.BeginAnimation(UIElement.OpacityProperty, null);
        ServerSwitchLabel.BeginAnimation(UIElement.OpacityProperty, null);
        ServerMapTransitionImage.Visibility = Visibility.Collapsed;
        ServerSwitchLabel.Visibility = Visibility.Collapsed;
        if (!SystemParameters.ClientAreaAnimation)
        {
            ServerMapTransitionImage.Source = null;
            return;
        }
        if (HasMapImage)
        {
            try { ServerMapTransitionImage.Source = CaptureServerMapTransition(WebViewHost); }
            catch { ServerMapTransitionImage.Source = null; }
        }
        if (ServerMapTransitionImage.Source != null)
        {
            ServerMapTransitionImage.Visibility = Visibility.Visible;
            ServerMapTransitionImage.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(120)));
        }
        ServerSwitchLabel.Visibility = Visibility.Visible;
        ServerSwitchLabel.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void CompleteServerMapTransition(int selectionVersion)
    {
        if (selectionVersion != _mapSelectionVersion) return;
        if (!SystemParameters.ClientAreaAnimation)
        {
            ServerMapTransitionImage.Source = null;
            ServerMapTransitionImage.Visibility = Visibility.Collapsed;
            ServerSwitchLabel.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            if (selectionVersion != _mapSelectionVersion) return;
            ServerMapTransitionImage.Visibility = Visibility.Collapsed;
            ServerMapTransitionImage.Source = null;
        };
        ServerMapTransitionImage.BeginAnimation(UIElement.OpacityProperty, fade);
        var labelFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { BeginTime = TimeSpan.FromMilliseconds(650) };
        labelFade.Completed += (_, _) =>
        {
            if (selectionVersion == _mapSelectionVersion) ServerSwitchLabel.Visibility = Visibility.Collapsed;
        };
        ServerSwitchLabel.BeginAnimation(UIElement.OpacityProperty, labelFade);
    }
}
