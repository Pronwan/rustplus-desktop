using System;
using System.Globalization;
using System.Windows.Data;

namespace RustPlusDesk.Converters;

public class LessThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramString && int.TryParse(paramString, out int compareValue))
        {
            return intValue < compareValue;
        }
        if (value is double doubleValue && parameter is string paramStringDouble && double.TryParse(paramStringDouble, out double compareValueDouble))
        {
            return doubleValue < compareValueDouble;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
