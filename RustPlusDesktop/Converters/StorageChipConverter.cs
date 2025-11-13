using RustPlusDesk.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    public sealed class StorageChipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not StorageSnapshot snap) return "–";
            if (snap.UpkeepSeconds is int s && s >= 0)
            {
                var ts = TimeSpan.FromSeconds(s);
                return $" {ts.Days}d {ts.Hours}h {ts.Minutes}m";
            }
            return $"{snap.Items?.Count ?? 0} Items";
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}

