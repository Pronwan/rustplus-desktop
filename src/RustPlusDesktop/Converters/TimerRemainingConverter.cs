using System;
using System.Globalization;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    public sealed class TimerRemainingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime endTime)
            {
                var remaining = endTime - DateTime.UtcNow;
                if (remaining.TotalSeconds <= 0)
                {
                    return "00:00:00";
                }
                
                if (remaining.TotalHours >= 1.0)
                    return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
                else
                    return $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
            return "00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
