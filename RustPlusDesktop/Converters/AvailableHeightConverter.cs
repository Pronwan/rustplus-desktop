using System;
using System.Globalization;
using System.Windows.Data;

namespace RustPlusDesk.Converters;

/// <summary>
/// Multi-value converter used to compute the height left over for one element
/// after its sibling has consumed part of a parent. Bind two ActualHeight
/// sources and pass a constant offset (margins / gap) as the converter
/// parameter:
/// <code>
///     MaxHeight = values[0] - values[1] - parameter
/// </code>
/// Keeps the result clamped to <c>>= 0</c> so layout never receives a negative
/// constraint while the parent is still measuring.
/// </summary>
public sealed class AvailableHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double total = (values?.Length > 0 && values[0] is double a) ? a : 0;
        double used  = (values?.Length > 1 && values[1] is double b) ? b : 0;
        double pad   = 0;
        if (parameter is double p) pad = p;
        else if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var pp)) pad = pp;
        var result = total - used - pad;
        return result < 0 ? 0 : result;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => Array.Empty<object>();
}
