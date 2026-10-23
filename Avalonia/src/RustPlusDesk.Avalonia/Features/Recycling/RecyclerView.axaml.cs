using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace RustPlusDesk.Avalonia.Features.Recycling;

public sealed partial class RecyclerView : UserControl
{
    public RecyclerView() => InitializeComponent();
    private void FilterChanged(object? sender, RoutedEventArgs e)
    { if (this.FindControl<ScrollViewer>("InputsScroll") is { } scroll) scroll.Offset = default; }
    private void InputsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is RecyclerViewModel model && sender is ScrollViewer scroll
            && e.OffsetDelta.Y > 0 && scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y < 200)
            model.LoadMoreCommand.Execute(null);
    }
    private static bool IsInput(object? source) => source is NumericUpDown || source is TextBox
        || source is Control control && control.GetVisualAncestors().Any(parent => parent is NumericUpDown or TextBox);
    private void ItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInput(e.Source) || sender is not Control { DataContext: RecyclerItemViewModel item } tile) return;
        var point = e.GetCurrentPoint(tile).Properties;
        int amount = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        if (point.IsLeftButtonPressed) item.Adjust(amount);
        else if (point.IsRightButtonPressed) item.Adjust(-amount);
        else return;
        e.Handled = true;
    }
    private void ItemWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsInput(e.Source) || sender is not Control { DataContext: RecyclerItemViewModel item } || e.Delta.Y == 0) return;
        item.Adjust(Math.Sign(e.Delta.Y) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1));
        e.Handled = true;
    }
}
