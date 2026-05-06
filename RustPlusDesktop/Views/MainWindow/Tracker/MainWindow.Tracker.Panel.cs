using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _trackerPanelOpen;

    private void BtnExpandTracker_Click(object sender, RoutedEventArgs e) => OpenTrackerPanel();

    private void BtnCloseTrackerPanel_Click(object sender, RoutedEventArgs e) => CloseTrackerPanel();

    private void OpenTrackerPanel()
    {
        if (_trackerPanelOpen) return;
        if (TrackerPanel == null || TrackerHostInTab == null || TrackerHostInPanel == null || TrackerContent == null)
            return;

        // Mutual exclusion — only one slide-in panel at a time.
        if (_reportPanelOpen) CloseInlineReport();

        // Reparent: tab → panel. The Tracker subtree keeps its bindings, x:Name
        // registrations, and event handlers because we move the same FrameworkElement
        // instance, we don't clone it.
        TrackerHostInTab.Content = null;
        TrackerHostInPanel.Content = TrackerContent;

        TrackerPanel.Visibility = Visibility.Visible;
        TrackerPanel.UpdateLayout();
        var w = TrackerPanel.ActualWidth > 0 ? TrackerPanel.ActualWidth : 750;
        if (TrackerPanelTransform != null) TrackerPanelTransform.X = w;

        this.PreviewKeyDown -= TrackerPanel_PreviewKeyDown;
        this.PreviewKeyDown += TrackerPanel_PreviewKeyDown;

        AnimateTrackerPanelTo(0);
        _trackerPanelOpen = true;
        OnSlideInPanelOpened();
    }

    private void CloseTrackerPanel()
    {
        if (!_trackerPanelOpen) return;
        if (TrackerPanel == null) return;

        var w = TrackerPanel.ActualWidth > 0 ? TrackerPanel.ActualWidth : 750;
        AnimateTrackerPanelTo(w, onCompleted: () =>
        {
            // Reparent: panel → tab.
            if (TrackerHostInPanel != null) TrackerHostInPanel.Content = null;
            if (TrackerHostInTab != null && TrackerContent != null)
                TrackerHostInTab.Content = TrackerContent;
            TrackerPanel.Visibility = Visibility.Collapsed;
        });
        this.PreviewKeyDown -= TrackerPanel_PreviewKeyDown;
        _trackerPanelOpen = false;
        OnSlideInPanelClosed();
    }

    private void TrackerPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Defer Esc to whichever modal is on top. Report and Commands both layer
        // above the Tracker (ZIndex 100 vs 50) — when one of them is open, its
        // own Esc handler should win and close it, leaving the Tracker visible.
        if (e.Key == Key.Escape && _trackerPanelOpen && !_reportPanelOpen)
        {
            CloseTrackerPanel();
            e.Handled = true;
        }
    }

    private void AnimateTrackerPanelTo(double targetX, Action? onCompleted = null)
    {
        if (TrackerPanelTransform == null) { onCompleted?.Invoke(); return; }
        var anim = new DoubleAnimation
        {
            To = targetX,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        if (onCompleted != null)
            anim.Completed += (_, __) => onCompleted();
        TrackerPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
