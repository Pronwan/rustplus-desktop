using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using RustPlusDesk.Models;

namespace RustPlusDesk.Converters;

public sealed class SwitchCommandDisplayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var text = GetCommandText(values);

        if (targetType == typeof(Visibility))
        {
            return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        if (string.Equals(parameter?.ToString(), "Raw", StringComparison.OrdinalIgnoreCase))
        {
            return text.TrimStart('!');
        }

        return text;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string GetCommandText(object[] values)
    {
        if (values.Length < 2 || values[0] is not SmartDevice device || values[1] is not ServerProfile profile)
        {
            return string.Empty;
        }

        if (!IsSwitch(device))
        {
            return string.Empty;
        }

        var mapping = profile.SwitchCommandMappings?.FirstOrDefault(m => m.EntityId == device.EntityId);
        var command = mapping?.Command;
        if (string.IsNullOrWhiteSpace(command))
            command = GeneratedSwitchCommand(profile, device);

        return string.IsNullOrWhiteSpace(command)
            ? string.Empty
            : $"{profile.ChatCommandPrefix}{command.Trim().TrimStart('!')}";
    }

    private static bool IsSwitch(SmartDevice device)
    {
        return string.Equals(device.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(device.Kind, "Smart Switch", StringComparison.OrdinalIgnoreCase);
    }

    private static string GeneratedSwitchCommand(ServerProfile profile, SmartDevice device)
    {
        var switches = profile.AllDevices.Where(IsSwitch).ToList();
        var index = switches.FindIndex(d => d.EntityId == device.EntityId);
        return index < 0 ? string.Empty : $"switch{index + 1}";
    }
}
