using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RustPlusDesk.Models; // falls SmartDevice hier liegt

namespace RustPlusDesk.Converters
{
    using System;
    using System.Globalization;
    using System.Windows.Data;
    using System.Windows.Media;
    using RustPlusDesk.Models; // wo StorageSnapshot liegt

    public class UpkeepBackgroundConverter : IValueConverter
    {
        private static readonly Brush DefaultBrush =
            new SolidColorBrush(Color.FromArgb(0x33, 0x40, 0xA0, 0x70)); // dein #3340A070

        private static readonly Brush OrangeBrush =
            new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xA5, 0x00)); // transparentes Orange

        private static readonly Brush RedBrush =
            new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x44, 0x44)); // transparentes Rot

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Wir erwarten hier direkt den StorageSnapshot
            if (value is not StorageSnapshot snap)
                return DefaultBrush;

            // Nur richtige TCs einfärben
            if (!snap.IsToolCupboard)
                return DefaultBrush;

            var secs = snap.UpkeepSeconds ?? 0;

            // 0 → Rot
            if (secs <= 0)
                return RedBrush;

            // < 1 Stunde → Orange
            if (secs < 3600)
                return OrangeBrush;

            // sonst Standard
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}