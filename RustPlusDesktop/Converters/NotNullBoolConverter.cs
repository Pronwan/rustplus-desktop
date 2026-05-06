using System;
using System.Globalization;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    public sealed class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) => value != null;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
