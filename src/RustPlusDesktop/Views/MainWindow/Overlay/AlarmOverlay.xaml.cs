using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RustPlusDesk.Views;

public partial class AlarmOverlay : UserControl
{
    public event EventHandler? PrevClicked;
    public event EventHandler? NextClicked;
    public event EventHandler? CloseClicked;
    public event EventHandler? AutoHideChanged;
    public event MouseButtonEventHandler? AutoHideCheckBoxClick;

    public AlarmOverlay()
    {
        InitializeComponent();
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => PrevClicked?.Invoke(this, EventArgs.Empty);
    private void Next_Click(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, EventArgs.Empty);
    private void Close_Click(object sender, RoutedEventArgs e) => CloseClicked?.Invoke(this, EventArgs.Empty);
    private void AutoHide_Changed(object sender, RoutedEventArgs e) => AutoHideChanged?.Invoke(this, EventArgs.Empty);
    private void OnAutoHideCheckBoxClick(object sender, MouseButtonEventArgs e) => AutoHideCheckBoxClick?.Invoke(this, e);
}
