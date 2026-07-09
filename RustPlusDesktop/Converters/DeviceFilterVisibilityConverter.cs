using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using RustPlusDesk.Models;

namespace RustPlusDesk.Converters;

public sealed class DeviceFilterVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not SmartDevice device)
            return Visibility.Visible;

        var search = values.Length > 1 ? values[1]?.ToString() : null;
        var type = values.Length > 2 ? values[2]?.ToString() : null;

        return Matches(device, search, type) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool Matches(SmartDevice device, string? search, string? type)
    {
        if (MatchesSelf(device, search, type))
            return true;

        return device.Children?.Any(child => Matches(child, search, type)) == true;
    }

    private static bool MatchesSelf(SmartDevice device, string? search, string? type)
    {
        if (!MatchesType(device, type))
            return false;

        if (string.IsNullOrWhiteSpace(search))
            return true;

        var q = search.Trim();
        return Contains(device.PureName, q) ||
               Contains(device.Name, q) ||
               Contains(device.Alias, q) ||
               Contains(device.Kind, q) ||
               device.EntityId.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesType(SmartDevice device, string? type)
    {
        return type switch
        {
            "switch" => IsKind(device, "SmartSwitch") || IsKind(device, "Smart Switch"),
            "alarm" => IsKind(device, "SmartAlarm") || IsKind(device, "Smart Alarm"),
            "storage" => IsKind(device, "StorageMonitor") || IsKind(device, "Storage Monitor"),
            _ => true
        };
    }

    private static bool IsKind(SmartDevice device, string kind)
        => string.Equals(device.Kind, kind, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string search)
        => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
}
