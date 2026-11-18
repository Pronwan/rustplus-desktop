using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private TaskCompletionSource? _serverMapTransitionCompletion;

    internal static Rect ServerMapTransitionBounds(FrameworkElement image, Visual viewport) =>
        image.ActualWidth > 0 && image.ActualHeight > 0
            ? image.TransformToAncestor(viewport).TransformBounds(new Rect(image.RenderSize))
            : Rect.Empty;

    private void BeginServerMapTransition()
    {
        _serverMapTransitionCompletion?.TrySetResult();
        ServerMapTransitionLayer.BeginAnimation(UIElement.OpacityProperty, null);
        ServerSwitchLabel.BeginAnimation(UIElement.OpacityProperty, null);
        ServerMapTransitionLayer.Visibility = Visibility.Collapsed;
        ServerSwitchLabel.Visibility = Visibility.Collapsed;
        if (!SystemParameters.ClientAreaAnimation)
        {
            ServerMapTransitionImage.Source = null;
            return;
        }
        if (HasMapImage)
        {
            // Reuse the decoded image; rendering the blurred visual tree to a bitmap stalled every switch.
            var bounds = ServerMapTransitionBounds(ImgMap, WebViewHost);
            ServerMapTransitionImage.Source = bounds.IsEmpty ? null : ImgMap.Source;
            if (!bounds.IsEmpty)
            {
                ServerMapTransitionImage.Width = bounds.Width;
                ServerMapTransitionImage.Height = bounds.Height;
                ServerMapTransitionImage.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);
                ServerMapTransitionLayer.Effect = _vm.IsMapPreviewVisible
                    ? new BlurEffect { Radius = 16, RenderingBias = RenderingBias.Performance } : null;
            }
        }
        if (ServerMapTransitionImage.Source != null)
        {
            ServerMapTransitionLayer.Visibility = Visibility.Visible;
            ServerMapTransitionLayer.Opacity = 1;
        }
        ServerSwitchLabel.Visibility = Visibility.Visible;
        ServerSwitchLabel.Opacity = 1;
    }

    private async Task CompleteServerMapTransitionAsync(int selectionVersion)
    {
        // Let layout and queued overlay work settle before starting the opacity fade.
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        if (selectionVersion != _mapSelectionVersion) return;
        if (!SystemParameters.ClientAreaAnimation || ServerMapTransitionImage.Source == null)
        {
            ServerMapTransitionImage.Source = null;
            ServerMapTransitionLayer.Visibility = Visibility.Collapsed;
            ServerMapTransitionLayer.Effect = null;
            ServerSwitchLabel.Visibility = Visibility.Collapsed;
            return;
        }
        var completion = _serverMapTransitionCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Timeline.SetDesiredFrameRate(fade, 60);
        fade.Completed += (_, _) =>
        {
            if (selectionVersion == _mapSelectionVersion)
            {
                ServerMapTransitionLayer.Visibility = Visibility.Collapsed;
                ServerMapTransitionImage.Source = null;
                ServerMapTransitionLayer.Effect = null;
            }
            completion.TrySetResult();
        };
        ServerMapTransitionLayer.BeginAnimation(UIElement.OpacityProperty, fade);
        var labelFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        labelFade.Completed += (_, _) =>
        {
            if (selectionVersion == _mapSelectionVersion) ServerSwitchLabel.Visibility = Visibility.Collapsed;
        };
        ServerSwitchLabel.BeginAnimation(UIElement.OpacityProperty, labelFade);
        await completion.Task;
    }
}
