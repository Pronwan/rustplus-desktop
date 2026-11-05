using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Helpers;

namespace RustPlusDesk.Views;

public partial class BusyOverlay : UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(BusyOverlay), new PropertyMetadata("Initializing…"));

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public BusyOverlay()
    {
        InitializeComponent();
        VersionText.Text = $"v{VersionHelper.GetClientVersion()}";
    }
}
