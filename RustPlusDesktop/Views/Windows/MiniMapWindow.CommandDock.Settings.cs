using System;
using System.Windows;
using RustPlusDesk.Models;

namespace RustPlusDesk
{
    /// <summary>
    /// The dock's own settings panel, as opposed to the map's.
    ///
    /// Two gears, two panels. The one in the title bar belongs to the window and opens this: the
    /// monitor it sits on, and the three looks that can be imposed on every tile at once. The
    /// one on the map tile opens the map's, where the shape, the size and the layers live.
    ///
    /// They used to be the same panel, which meant a dock with no map on it offered settings for
    /// a map — and none of them reached a single tile.
    /// </summary>
    public partial class MiniMapWindow
    {
        /// <summary>
        /// Points the dock settings panel at this window, once.
        ///
        /// Every write the panel makes goes through its ParentWindow, so a null one is a panel
        /// whose switches do nothing and say nothing about it. It is set when the window is
        /// built rather than only when the panel is opened, so there is no order of events that
        /// leaves it unset.
        /// </summary>
        private void BindDockSettingsPanel()
        {
            if (DockSettingsOverlay != null) DockSettingsOverlay.ParentWindow = this;
        }

        public void OpenDockSettings()
        {
            if (DockSettingsPopup.IsOpen) return;

            BindDockSettingsPanel();

            // Beside the dock rather than beside the map: this one belongs to the whole window,
            // and the map may not be on it.
            const double gap = 12;
            const double panelWidth = 250;
            const double panelHeight = 430;

            var screen = ScreenBoundsFor(this);

            double offsetX = ActualWidth + gap;
            if (Left + offsetX + panelWidth > screen.Right) offsetX = -panelWidth - gap;
            if (Left + offsetX < screen.Left) offsetX = Math.Max(screen.Left - Left, ActualWidth + gap);

            double offsetY = 0;
            if (Top + offsetY + panelHeight > screen.Bottom)
                offsetY = Math.Min(0, screen.Bottom - panelHeight - Top);
            if (Top + offsetY < screen.Top) offsetY = screen.Top - Top;

            DockSettingsOverlay.Load(_dock, CurrentMonitorDeviceName());

            DockSettingsPopup.HorizontalOffset = offsetX;
            DockSettingsPopup.VerticalOffset = offsetY;
            DockSettingsPopup.IsOpen = true;
        }

        public void CloseDockSettings() => DockSettingsPopup.IsOpen = false;

        /// <summary>Fills the panel again, after something changed it from underneath.</summary>
        public void ReloadDockSettingsPanel()
        {
            if (!DockSettingsPopup.IsOpen) return;

            DockSettingsOverlay.Load(_dock, CurrentMonitorDeviceName());
        }

        /// <summary>
        /// Changes something about how the dock looks, saves it, and redraws.
        ///
        /// A rebuild rather than a refresh: opacity, text size and colour are read when a tile is
        /// built, by every builder, and there is no refresher that re-reads them.
        /// </summary>
        public void UpdateDockAppearance(Action<CommandDockLayout> change)
        {
            change(_dock);

            SaveDock();
            RebuildTiles();
        }

        /// <summary>Which screen the dock is on, for the panel's dropdown.</summary>
        private string? CurrentMonitorDeviceName()
        {
            try
            {
                var middle = new System.Drawing.Point(
                    (int)(Left + ActualWidth / 2), (int)(Top + ActualHeight / 2));

                return System.Windows.Forms.Screen.FromPoint(middle).DeviceName;
            }
            catch
            {
                return null;
            }
        }
    }
}
