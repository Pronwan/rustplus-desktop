using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RustPlusDesk.Models;

namespace RustPlusDesk.Converters
{
    /// <summary>
    /// A clock that ticks once a minute, so relative times ("5 minutes ago") bound to it move on
    /// while the list sits open without the whole list being rebuilt.
    /// </summary>
    public sealed class MinuteClock : INotifyPropertyChanged
    {
        public static MinuteClock Instance { get; } = new();

        private readonly DispatcherTimer _timer;

        private MinuteClock()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Now)));
            _timer.Start();
        }

        public DateTime Now => DateTime.Now;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// "just now", "5 minutes ago", "2 hours ago", "6 days ago" — the live map's wording — and a
    /// plain date once a notification is older than a month. Bound with the timestamp first and
    /// <see cref="MinuteClock.Now"/> second.
    /// </summary>
    public sealed class RelativeTimeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 0 || values[0] is not DateTime when) return string.Empty;

            if (when.Kind == DateTimeKind.Utc) when = when.ToLocalTime();
            var age = DateTime.Now - when;

            if (age.TotalSeconds < 45) return Text("RelTimeJustNow", "just now");
            if (age.TotalMinutes < 60) return Plural((int)Math.Max(1, Math.Round(age.TotalMinutes)), "RelTimeMinute", "{0} minute ago", "RelTimeMinutes", "{0} minutes ago");
            if (age.TotalHours < 24) return Plural((int)age.TotalHours, "RelTimeHour", "{0} hour ago", "RelTimeHours", "{0} hours ago");
            if (age.TotalDays < 30) return Plural((int)age.TotalDays, "RelTimeDay", "{0} day ago", "RelTimeDays", "{0} days ago");
            return when.ToString("d MMM yyyy", culture);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static string Plural(int n, string oneKey, string one, string manyKey, string many) =>
            string.Format(n == 1 ? Text(oneKey, one) : Text(manyKey, many), n);

        private static string Text(string key, string fallback) =>
            RustPlusDesk.Helpers.Loc.TextOrNull(key) ?? fallback;
    }

    /// <summary>
    /// Turns a picture URL into an image that downloads in the background, and hands back the
    /// same image for the same URL so scrolling or a refresh never fetches it twice. A broken
    /// URL simply shows nothing, leaving the row's icon tile underneath visible.
    /// </summary>
    public sealed class UrlToImageConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new();

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string url || string.IsNullOrWhiteSpace(url)) return null;

            return Cache.GetOrAdd(url, static u =>
            {
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return null;
                }

                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = uri;
                    image.DecodePixelWidth = 112; // twice the 56px tile, for high-DPI screens
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    return image;
                }
                catch
                {
                    return null;
                }
            });
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// The clickable server tag under a row, shown only when it adds something: a known server
    /// whose name is not already the row's title or message.
    /// </summary>
    public sealed class NotificationServerTagVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not RustPlusNotification n || string.IsNullOrEmpty(n.ServerIp)) return Visibility.Collapsed;

            var name = n.ServerName;
            bool alreadyShown = !string.IsNullOrWhiteSpace(name) &&
                                (string.Equals(name, n.Title, StringComparison.Ordinal) ||
                                 string.Equals(name, n.Message, StringComparison.Ordinal));
            return alreadyShown ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
