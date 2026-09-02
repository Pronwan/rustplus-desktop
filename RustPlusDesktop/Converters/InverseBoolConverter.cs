using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    /// <summary>
    /// Negates a bool, in both directions.
    ///
    /// Written for pairs of radio buttons that describe one either/or setting: the setting is a
    /// single bool, and the second button needs the opposite of it while still writing back when
    /// it is picked. An unset or non-bool value counts as false, so a missing profile shows the
    /// first option selected rather than neither.
    /// </summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => !(value is bool b && b);

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => !(value is bool b && b);
    }
}
