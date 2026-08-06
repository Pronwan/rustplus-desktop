using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RustPlusDesk.Features.Tutorials;

public interface IWebViewTutorialBridge
{
    Task<Rect?> GetTargetBoundsAsync(string targetId, FrameworkElement relativeTo, CancellationToken cancellationToken = default);
}

public interface ITutorialTargetResolver
{
    Task<TutorialTarget?> ResolveAsync(TutorialStep step, FrameworkElement root, CancellationToken cancellationToken = default);
}

public sealed class TutorialTargetResolver(IWebViewTutorialBridge? webViewBridge = null) : ITutorialTargetResolver
{
    public async Task<TutorialTarget?> ResolveAsync(TutorialStep step, FrameworkElement root, CancellationToken cancellationToken = default)
    {
        if (step.WebViewTargetId is not null && webViewBridge is not null)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Rect? webBounds = await webViewBridge.GetTargetBoundsAsync(step.WebViewTargetId, root, cancellationToken);
                if (webBounds is not null) return new TutorialTarget(webBounds.Value);
                await Task.Delay(100, cancellationToken);
            }
            return null;
        }

        if (step.TargetId is null) return new TutorialTarget(Rect.Empty);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindTarget(root, step.TargetId) is FrameworkElement element && element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0)
            {
                if (step.AutoScrollIntoView) element.BringIntoView();
                await root.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                Point origin = element.TransformToAncestor(root).Transform(new Point());
                var bounds = new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
                bounds = ApplyPadding(bounds, step.SpotlightPadding);
                return new TutorialTarget(bounds, element);
            }
            await Task.Delay(75, cancellationToken);
            await root.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded, cancellationToken);
        }
        return null;
    }

    private static FrameworkElement? FindTarget(DependencyObject parent, string targetId)
    {
        if (parent is FrameworkElement element && !Tutorial.GetIgnore(element))
        {
            if (string.Equals(Tutorial.GetTargetId(element), targetId, StringComparison.Ordinal) ||
                string.Equals(element.Name, targetId, StringComparison.Ordinal))
                return element;
        }

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
            if (FindTarget(VisualTreeHelper.GetChild(parent, i), targetId) is { } found) return found;
        return null;
    }

    private static Rect ApplyPadding(Rect rect, Thickness padding) => new(
        Math.Max(0, rect.X - padding.Left),
        Math.Max(0, rect.Y - padding.Top),
        rect.Width + padding.Left + padding.Right,
        rect.Height + padding.Top + padding.Bottom);
}
