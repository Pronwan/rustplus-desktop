using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RustPlusDesk.Converters;

public sealed class DeviceTreeItemWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double treeWidth || treeWidth <= 0)
        {
            return DependencyProperty.UnsetValue;
        }

        var item = values[1] as TreeViewItem;
        var (outerPadding, childIndent, minWidth) = Parse(parameter as string);
        var width = treeWidth - outerPadding - GetDepth(item) * childIndent;

        return Math.Max(minWidth, width);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static int GetDepth(DependencyObject? item)
    {
        var depth = 0;
        var parent = item == null ? null : VisualTreeHelper.GetParent(item);

        while (parent != null)
        {
            if (parent is TreeViewItem)
            {
                depth++;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return depth;
    }

    private static (double outerPadding, double childIndent, double minWidth) Parse(string? parameter)
    {
        var outerPadding = 36d;
        var childIndent = 26d;
        var minWidth = 220d;

        if (string.IsNullOrWhiteSpace(parameter))
        {
            return (outerPadding, childIndent, minWidth);
        }

        var parts = parameter.Split(',');
        if (parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedOuter))
        {
            outerPadding = parsedOuter;
        }

        if (parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedIndent))
        {
            childIndent = parsedIndent;
        }

        if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMin))
        {
            minWidth = parsedMin;
        }

        return (outerPadding, childIndent, minWidth);
    }
}
