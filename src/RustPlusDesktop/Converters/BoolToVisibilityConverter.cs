using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = false;
            if (value is bool v) b = v;
            else if (value is int i) b = i > 0;
            else if (value is uint ui) b = ui > 0;
            else if (value is double d) b = d > 0;

            // Param-Unterstützung, ohne vorhandene Invert-Verwendungen zu brechen:
            bool invert = Invert;
            if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
                invert = !invert;

            if (invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is Visibility vis && vis == Visibility.Visible) ^ Invert;
    }
}