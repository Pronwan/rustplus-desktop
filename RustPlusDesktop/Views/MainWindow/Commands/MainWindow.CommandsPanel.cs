using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _commandsPanelOpen;

    private void BtnCommandsHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_commandsPanelOpen) CloseCommandsPanel();
        else OpenCommandsPanel();
    }

    private void BtnCloseCommands_Click(object sender, RoutedEventArgs e) => CloseCommandsPanel();

    private void OpenCommandsPanel()
    {
        if (CommandsPanel == null) return;

        CommandsPanel.Visibility = Visibility.Visible;
        CommandsPanel.UpdateLayout();
        var w = CommandsPanel.ActualWidth > 0 ? CommandsPanel.ActualWidth : 700;
        if (CommandsPanelTransform != null) CommandsPanelTransform.X = w;

        this.PreviewKeyDown -= CommandsPanel_PreviewKeyDown;
        this.PreviewKeyDown += CommandsPanel_PreviewKeyDown;

        AnimateCommandsPanelTo(0);
        _commandsPanelOpen = true;
    }

    private void CloseCommandsPanel()
    {
        if (CommandsPanel == null || !_commandsPanelOpen) return;
        var w = CommandsPanel.ActualWidth > 0 ? CommandsPanel.ActualWidth : 700;
        AnimateCommandsPanelTo(w, onCompleted: () =>
        {
            CommandsPanel.Visibility = Visibility.Collapsed;
        });
        this.PreviewKeyDown -= CommandsPanel_PreviewKeyDown;
        _commandsPanelOpen = false;
    }

    private void CommandsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_commandsPanelOpen && CommandsPanelTransform != null)
            CommandsPanelTransform.X = e.NewSize.Width;
    }

    private void CommandsPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _commandsPanelOpen)
        {
            CloseCommandsPanel();
            e.Handled = true;
        }
    }

    private void AnimateCommandsPanelTo(double targetX, Action? onCompleted = null)
    {
        if (CommandsPanelTransform == null) { onCompleted?.Invoke(); return; }
        var anim = new DoubleAnimation
        {
            To = targetX,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        if (onCompleted != null) anim.Completed += (_, __) => onCompleted();
        CommandsPanelTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
