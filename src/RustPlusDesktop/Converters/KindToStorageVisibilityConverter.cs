// using System;
// using System.Globalization;
// using System.Windows;
// using System.Windows.Data;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace RustPlusDesk.Converters { 
public sealed class KindToStorageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var k = value as string;
        return (string.Equals(k, "StorageMonitor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k, "Storage Monitor", StringComparison.OrdinalIgnoreCase))
               ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
}