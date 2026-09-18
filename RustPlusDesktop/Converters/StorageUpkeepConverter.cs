using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RustPlusDesk.Models;

namespace RustPlusDesk.Converters
{
    public sealed class StorageUpkeepConverter : IValueConverter
    {
        // Safe (Green)
        private static readonly Brush BgSafe = new SolidColorBrush(Color.FromArgb(0xEA, 0x11, 0x2A, 0x1E));
        private static readonly Brush BorderSafe = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0xB9, 0x81));
        private static readonly Brush FgSafe = new SolidColorBrush(Color.FromArgb(0xFF, 0x34, 0xD3, 0x99));

        // Warning (Amber - < 24h)
        private static readonly Brush BgWarning = new SolidColorBrush(Color.FromArgb(0xEA, 0x2D, 0x21, 0x0C));
        private static readonly Brush BorderWarning = new SolidColorBrush(Color.FromArgb(0xD0, 0xF5, 0x9E, 0x0B));
        private static readonly Brush FgWarning = new SolidColorBrush(Color.FromArgb(0xFF, 0xFB, 0xBF, 0x24));

        // Critical (Red - < 1h or 0)
        private static readonly Brush BgDanger = new SolidColorBrush(Color.FromArgb(0xEA, 0x33, 0x14, 0x17));
        private static readonly Brush BorderDanger = new SolidColorBrush(Color.FromArgb(0xD0, 0xEF, 0x44, 0x44));
        private static readonly Brush FgDanger = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71));

        // Default / Generic container (Blue / Slate)
        private static readonly Brush BgDefault = new SolidColorBrush(Color.FromArgb(0xEA, 0x16, 0x1E, 0x2B));
        private static readonly Brush BorderDefault = new SolidColorBrush(Color.FromArgb(0xD0, 0x3B, 0x82, 0xF6));
        private static readonly Brush FgDefault = new SolidColorBrush(Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA));

        static StorageUpkeepConverter()
        {
            BgSafe.Freeze();
            BorderSafe.Freeze();
            FgSafe.Freeze();
            BgWarning.Freeze();
            BorderWarning.Freeze();
            FgWarning.Freeze();
            BgDanger.Freeze();
            BorderDanger.Freeze();
            FgDanger.Freeze();
            BgDefault.Freeze();
            BorderDefault.Freeze();
            FgDefault.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var param = parameter as string ?? "status_text";

            if (value is not StorageSnapshot snap)
            {
                return param switch
                {
                    "badge_bg" => BgDefault,
                    "badge_border" => BorderDefault,
                    "badge_fg" => FgDefault,
                    "status_text" => "STORAGE",
                    "status_icon" => Wpf.Ui.Controls.SymbolRegular.Box20,
                    "upkeep_text" => "–",
                    "is_tc_vis" => Visibility.Collapsed,
                    "is_not_tc_vis" => Visibility.Visible,
                    _ => string.Empty
                };
            }

            if (!snap.IsToolCupboard)
            {
                return param switch
                {
                    "badge_bg" => BgDefault,
                    "badge_border" => BorderDefault,
                    "badge_fg" => FgDefault,
                    "status_text" => "STORAGE MONITOR",
                    "status_icon" => Wpf.Ui.Controls.SymbolRegular.Box20,
                    "upkeep_text" => snap.ItemsCount == 1 ? "1 item" : $"{snap.ItemsCount} items",
                    "is_tc_vis" => Visibility.Collapsed,
                    "is_not_tc_vis" => Visibility.Visible,
                    _ => string.Empty
                };
            }

            var secs = snap.UpkeepSeconds ?? 0;

            if (param == "is_tc_vis") return Visibility.Visible;
            if (param == "is_not_tc_vis") return Visibility.Collapsed;

            if (secs <= 0)
            {
                return param switch
                {
                    "badge_bg" => BgDanger,
                    "badge_border" => BorderDanger,
                    "badge_fg" => FgDanger,
                    "status_text" => "DECAYING",
                    "status_icon" => Wpf.Ui.Controls.SymbolRegular.Warning20,
                    "upkeep_text" => "0s (Decaying)",
                    _ => "DECAYING"
                };
            }

            if (secs < 3600)
            {
                return param switch
                {
                    "badge_bg" => BgDanger,
                    "badge_border" => BorderDanger,
                    "badge_fg" => FgDanger,
                    "status_text" => "CRITICAL",
                    "status_icon" => Wpf.Ui.Controls.SymbolRegular.Warning20,
                    "upkeep_text" => FormatTime(secs),
                    _ => "CRITICAL"
                };
            }

            if (secs < 86400)
            {
                return param switch
                {
                    "badge_bg" => BgWarning,
                    "badge_border" => BorderWarning,
                    "badge_fg" => FgWarning,
                    "status_text" => "WARNING",
                    "status_icon" => Wpf.Ui.Controls.SymbolRegular.Clock20,
                    "upkeep_text" => FormatTime(secs),
                    _ => "WARNING"
                };
            }

            return param switch
            {
                "badge_bg" => BgSafe,
                "badge_border" => BorderSafe,
                "badge_fg" => FgSafe,
                "status_text" => "PROTECTED",
                "status_icon" => Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark20,
                "upkeep_text" => FormatTime(secs),
                _ => "PROTECTED"
            };
        }

        private static string FormatTime(int secs)
        {
            int days = secs / 86400;
            int rem = secs % 86400;
            int hours = rem / 3600;
            rem = rem % 3600;
            int mins = rem / 60;
            int secsLeft = rem % 60;

            var parts = new List<string>();
            if (days > 0) parts.Add($"{days}d");
            if (hours > 0) parts.Add($"{hours}h");
            if (mins > 0) parts.Add($"{mins}m");
            if (parts.Count == 0 && secsLeft > 0) parts.Add($"{secsLeft}s");
            if (parts.Count == 0) parts.Add("0s");

            return string.Join(" ", parts);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
