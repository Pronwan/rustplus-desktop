using RustPlusDesk.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace RustPlusDesk.Converters
{
    public sealed class StorageChipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not StorageSnapshot snap)
                return string.Empty;

            // TC: immer Upkeep – egal ob 0 oder >0
            if (snap.IsToolCupboard)
            {
                var secs = snap.UpkeepSeconds ?? 0;

                // Sonderfall: 0 → mit Prefix "Upkeep: 0s"
                if (secs <= 0)
                    return "Upkeep: 0s";

                // Ab hier: nur noch kompakte Dauer ohne "Upkeep:"
                int days = secs / 86400;
                int rem = secs % 86400;
                int hours = rem / 3600;
                rem = rem % 3600;
                int mins = rem / 60;
                int secsLeft = rem % 60;

                var parts = new List<string>();

                if (days > 0)
                    parts.Add($"{days}d");
                if (hours > 0)
                    parts.Add($"{hours}h");
                if (mins > 0)
                    parts.Add($"{mins}m");

                // Falls alles auf 0, aber >0 Sekunden übrig, z. B. 45s
                if (parts.Count == 0 && secsLeft > 0)
                    parts.Add($"{secsLeft}s");

                // Sicherstellen, dass wir überhaupt etwas anzeigen
                if (parts.Count == 0)
                    parts.Add("0s");

                return string.Join(" ", parts);
            }

            // Alles andere: Boxen, Container → Items
            var count = snap.ItemsCount;
            return count == 1 ? "1 item" : $"{count} items";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

