using System;
using System.Windows;

namespace RustPlusDesk.Helpers
{
    /// <summary>
    /// Centres a dialog on the window that opened it, and keeps it there.
    ///
    /// WindowStartupLocation.CenterOwner only does half of this: it positions the
    /// dialog once, at open time, and only when an Owner was actually set. A dialog
    /// created without one lands in the middle of the primary screen instead, which
    /// is the wrong monitor as often as not. Moving or resizing the owner afterwards
    /// leaves the dialog behind either way, so the owner's own moves are followed for
    /// as long as the dialog is open.
    /// </summary>
    public static class OwnerCentering
    {
        /// <summary>
        /// Own <paramref name="dialog"/> by <paramref name="owner"/>, centre it there,
        /// and re-centre it whenever the owner moves, resizes or is maximised.
        /// </summary>
        public static void CenterOnOwner(this Window? dialog, Window? owner)
        {
            if (dialog is null || owner is null || ReferenceEquals(dialog, owner))
                return;

            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            EventHandler onOwnerChanged = (_, _) => Center(dialog, owner);
            SizeChangedEventHandler onSizeChanged = (_, _) => Center(dialog, owner);

            owner.LocationChanged += onOwnerChanged;
            owner.StateChanged += onOwnerChanged;
            owner.SizeChanged += onSizeChanged;

            // A dialog that sizes itself to its content is still the wrong size when
            // CenterOwner runs, so the first correct centring is this one.
            dialog.SizeChanged += onSizeChanged;

            dialog.Closed += (_, _) =>
            {
                owner.LocationChanged -= onOwnerChanged;
                owner.StateChanged -= onOwnerChanged;
                owner.SizeChanged -= onSizeChanged;
            };
        }

        private static void Center(Window dialog, Window owner)
        {
            if (owner.WindowState == WindowState.Minimized || !owner.IsVisible)
                return;
            if (owner.ActualWidth <= 0 || dialog.ActualWidth <= 0)
                return;

            // Left/Top report the restore bounds while the owner is maximised, so the
            // owner is asked where it is rather than where it would be if restored.
            var source = PresentationSource.FromVisual(owner);
            if (source?.CompositionTarget is null)
                return;

            Point origin;
            try
            {
                origin = source.CompositionTarget.TransformFromDevice.Transform(
                    owner.PointToScreen(new Point(0, 0)));
            }
            catch (InvalidOperationException)
            {
                // The owner lost its presentation source between the check and the call.
                return;
            }

            var left = origin.X + ((owner.ActualWidth - dialog.ActualWidth) / 2);
            var top = origin.Y + ((owner.ActualHeight - dialog.ActualHeight) / 2);

            // An owner dragged half off-screen would otherwise take a modal dialog with
            // it, and a modal that cannot be reached cannot be dismissed either.
            var minLeft = SystemParameters.VirtualScreenLeft;
            var minTop = SystemParameters.VirtualScreenTop;
            var maxLeft = minLeft + SystemParameters.VirtualScreenWidth - dialog.ActualWidth;
            var maxTop = minTop + SystemParameters.VirtualScreenHeight - dialog.ActualHeight;

            dialog.Left = maxLeft > minLeft ? Math.Clamp(left, minLeft, maxLeft) : minLeft;
            dialog.Top = maxTop > minTop ? Math.Clamp(top, minTop, maxTop) : minTop;
        }
    }
}
