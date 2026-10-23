using Avalonia.Controls;
using Avalonia.Threading;

namespace RustPlusDesk.Avalonia.Features.Servers;

public sealed partial class ServerEditorView : UserControl
{
    public ServerEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not null && IsEffectivelyVisible) ServerName.Focus();
        });
    }
}
