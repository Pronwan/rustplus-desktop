using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    public sealed class ItemsCountConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is System.Collections.IEnumerable e) { int n = 0; foreach (var _ in e) n++; return n; }
            return 0;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}