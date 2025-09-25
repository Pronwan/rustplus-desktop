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
            var b = value is bool v && v;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is Visibility v && v == Visibility.Visible) ^ Invert;
    }
}