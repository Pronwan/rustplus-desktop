using Avalonia.Controls;
using Avalonia.Threading;

namespace RustPlusDesk.Avalonia.Features.Accounts;

public sealed partial class CompanionAccountView : UserControl
{
    public CompanionAccountView()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && IsVisible)
                Dispatcher.UIThread.Post(() => { if (IsEffectivelyVisible) CheckStatus.Focus(); });
        };
    }
}
