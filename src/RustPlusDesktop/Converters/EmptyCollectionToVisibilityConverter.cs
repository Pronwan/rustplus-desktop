using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    public sealed class EmptyCollectionToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty = true;
            if (value is ICollection col)
            {
                isEmpty = col.Count == 0;
            }
            else if (value is IEnumerable enumerable)
            {
                isEmpty = !enumerable.GetEnumerator().MoveNext();
            }

            if (Invert) isEmpty = !isEmpty;
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
