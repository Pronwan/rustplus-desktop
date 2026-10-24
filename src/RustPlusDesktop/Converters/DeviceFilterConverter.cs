using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using RustPlusDesk.Models;

namespace RustPlusDesk.Converters
{
    public sealed class DeviceFilterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable<SmartDevice> devices && parameter is string kind)
            {
                if (kind.Equals("SmartSwitch", StringComparison.OrdinalIgnoreCase))
                {
                    return devices.Where(d => string.Equals(d.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) || 
                                              string.Equals(d.Kind, "Smart Switch", StringComparison.OrdinalIgnoreCase)).ToList();
                }
                if (kind.Equals("SmartAlarm", StringComparison.OrdinalIgnoreCase))
                {
                    return devices.Where(d => string.Equals(d.Kind, "SmartAlarm", StringComparison.OrdinalIgnoreCase) || 
                                              string.Equals(d.Kind, "Smart Alarm", StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
