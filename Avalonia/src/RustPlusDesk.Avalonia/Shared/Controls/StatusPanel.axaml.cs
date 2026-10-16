using Avalonia;
using Avalonia.Controls;

namespace RustPlusDesk.Avalonia.Shared.Controls;

/// <summary>A shared empty-state and error-state panel.</summary>
public sealed partial class StatusPanel : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<StatusPanel, string>(nameof(Title), "");
    public static readonly StyledProperty<string> MessageProperty = AvaloniaProperty.Register<StatusPanel, string>(nameof(Message), "");

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    public StatusPanel() => InitializeComponent();
}
