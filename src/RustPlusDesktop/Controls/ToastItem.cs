using System;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace RustPlusDesk.Controls
{
    /// <summary>
    /// One entry in the stacked toast host. This is a plain data record bound by
    /// <c>ToastHost</c> (an ItemsControl); all timing and enter/exit animation is
    /// driven from MainWindow. Toasts are add-once / dismiss-once — to "update" a
    /// persistent toast (e.g. the pairing guide) we dismiss it and add a fresh one,
    /// which keeps this type free of change-notification plumbing.
    /// </summary>
    public sealed class ToastItem
    {
        /// <summary>Bold heading. Hidden when empty.</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Plain body text, shown when <see cref="Content"/> is null.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>Optional rich body (buttons etc.). Takes the place of <see cref="Message"/>.</summary>
        public object? Content { get; set; }

        public SymbolRegular Icon { get; init; } = SymbolRegular.Info24;

        /// <summary>Colour of the left accent bar and icon — encodes the severity.</summary>
        public Brush AccentBrush { get; init; } = Brushes.SteelBlue;

        public double MaxCardWidth { get; init; } = 420;

        public bool ShowClose { get; init; } = true;

        /// <summary>Auto-dismiss delay. <see cref="TimeSpan.Zero"/> pins the toast until dismissed in code.</summary>
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Runs exactly once when the toast leaves the screen for any reason —
        /// auto-timeout, the close button, or a programmatic dismiss. Used e.g. to
        /// stop the offline-death alarm sound whichever way its toast goes away.
        /// </summary>
        public Action? Closed { get; set; }

        /// <summary>Per-toast auto-dismiss timer, owned by the host.</summary>
        internal DispatcherTimer? Timer { get; set; }
    }
}
