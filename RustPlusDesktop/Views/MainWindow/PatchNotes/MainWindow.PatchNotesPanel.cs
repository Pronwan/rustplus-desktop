using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _patchNotesPanelOpen;

    private void OpenPatchNotesPanel()
    {
        if (_patchNotesPanelOpen) return;
        if (PatchNotesPanel == null) return;

        // Mutual exclusion with the other right-column slide-ins so we never
        // stack panels on top of each other.
        if (_reportPanelOpen) CloseInlineReport();
        if (_trackerPanelOpen) CloseTrackerPanel();

        PatchNotesPanel.Visibility = Visibility.Visible;
        // Invalidate-then-update so the inner ScrollViewer measures against
        // the *current* right-column height. Visibility=Collapsed elements
        // skip layout entirely and can come back with stale cached extents,
        // which left the patch-notes scroll dead until something else (like
        // a server connect) forced a fresh layout pass.
        PatchNotesPanel.InvalidateMeasure();
        PatchNotesPanel.UpdateLayout();
        PatchNotesScroll?.InvalidateMeasure();

        var w = PatchNotesPanel.ActualWidth > 0 ? PatchNotesPanel.ActualWidth : 720;
        if (PatchNotesPanelTransform != null) PatchNotesPanelTransform.X = w;

        this.PreviewKeyDown -= PatchNotesPanel_PreviewKeyDown;
        this.PreviewKeyDown += PatchNotesPanel_PreviewKeyDown;

        AnimatePatchNotesPanelTo(0);
        _patchNotesPanelOpen = true;
    }

    private void ClosePatchNotesPanel()
    {
        if (!_patchNotesPanelOpen) return;
        if (PatchNotesPanel == null) return;
        var w = PatchNotesPanel.ActualWidth > 0 ? PatchNotesPanel.ActualWidth : 720;
        AnimatePatchNotesPanelTo(w, onCompleted: () =>
        {
            PatchNotesPanel.Visibility = Visibility.Collapsed;
        });
        this.PreviewKeyDown -= PatchNotesPanel_PreviewKeyDown;
        _patchNotesPanelOpen = false;
    }

    private void BtnClosePatchNotesPanel_Click(object sender, RoutedEventArgs e) => ClosePatchNotesPanel();

    private void PatchNotesPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _patchNotesPanelOpen)
        {
            ClosePatchNotesPanel();
            e.Handled = true;
        }
    }

    private void AnimatePatchNotesPanelTo(double targetX, Action? onCompleted = null)
    {
        if (PatchNotesPanelTransform == null) { onCompleted?.Invoke(); return; }
        var anim = new DoubleAnimation
        {
            To = targetX,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        if (onCompleted != null)
            anim.Completed += (_, __) => onCompleted();
        PatchNotesPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
