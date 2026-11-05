using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using RustPlusDesk.Models;

namespace RustPlusDesk.Converters;

public sealed class DeviceHotkeyDisplayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var text = GetHotkeyText(values);

        if (targetType == typeof(Visibility))
        {
            return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        return text;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string GetHotkeyText(object[] values)
    {
        if (values.Length < 2 || values[0] is not SmartDevice device)
        {
            return string.Empty;
        }

        var profile = values.Length > 2 ? values[2] as ServerProfile : null;
        var hotkeys = values[1] as IReadOnlyDictionary<string, List<long>>;
        var gesture = FindGesture(hotkeys, device.EntityId);

        if (string.IsNullOrWhiteSpace(gesture) && profile != null)
            gesture = FindGesture(LoadHotkeys(profile), device.EntityId);

        return string.IsNullOrWhiteSpace(gesture) ? string.Empty : CompactGesture(gesture);
    }

    private static string? FindGesture(IReadOnlyDictionary<string, List<long>>? hotkeys, uint entityId)
    {
        return hotkeys?.FirstOrDefault(kv => kv.Value?.Contains(entityId) == true).Key;
    }

    private static string CompactGesture(string gesture)
    {
        return gesture
            .Replace("Control", "Ctrl", StringComparison.OrdinalIgnoreCase)
            .Replace("D0", "0", StringComparison.OrdinalIgnoreCase)
            .Replace("D1", "1", StringComparison.OrdinalIgnoreCase)
            .Replace("D2", "2", StringComparison.OrdinalIgnoreCase)
            .Replace("D3", "3", StringComparison.OrdinalIgnoreCase)
            .Replace("D4", "4", StringComparison.OrdinalIgnoreCase)
            .Replace("D5", "5", StringComparison.OrdinalIgnoreCase)
            .Replace("D6", "6", StringComparison.OrdinalIgnoreCase)
            .Replace("D7", "7", StringComparison.OrdinalIgnoreCase)
            .Replace("D8", "8", StringComparison.OrdinalIgnoreCase)
            .Replace("D9", "9", StringComparison.OrdinalIgnoreCase)
            .Replace("OemPlus", "+", StringComparison.OrdinalIgnoreCase)
            .Replace("OemMinus", "-", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, List<long>>? LoadHotkeys(ServerProfile profile)
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustPlusDesk",
            "hotkeys.json");

        if (!System.IO.File.Exists(path))
            return null;

        var json = System.IO.File.ReadAllText(path);
        var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<long>>>>(json);
        return all != null && all.TryGetValue($"{profile.Host}:{profile.Port}", out var map) ? map : null;
    }
}
