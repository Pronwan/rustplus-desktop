using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;

namespace RustPlusDesk.Converters
{
    public sealed class KindToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var kind = (value as string ?? string.Empty)
                       .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
                       .ToLowerInvariant();

            string? key = kind switch
            {
                "storagemonitor" => "IconStorageMonitor",
                "smartswitch" => "IconSmartSwitch",
                "smartalarm" => "IconSmartAlarm",
                _ => null
            };

            if (key is null) return Binding.DoNothing;

            // TryFindResource wirft keine Exception, wenn Key fehlt
            var res = Application.Current?.TryFindResource(key);
            return res ?? Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
