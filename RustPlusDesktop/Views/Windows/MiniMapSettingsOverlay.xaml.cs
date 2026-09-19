using System;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class MiniMapSettingsOverlay : UserControl
    {
        public MiniMapWindow? ParentWindow { get; set; }
        private bool _isInitializing = false;

        public MiniMapSettingsOverlay()
        {
            InitializeComponent();
            Loaded += MiniMapSettingsOverlay_Loaded;
        }

        private bool _loadedOnce;

        private void MiniMapSettingsOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            // A popup's child is disconnected when it closes, so this fires again on every open.
            // Re-reading the file each time would overwrite whatever the user has changed since
            // with what was on disk when the panel first appeared.
            if (_loadedOnce) return;
            _loadedOnce = true;

            _isInitializing = true;
            try
            {
                var settings = StorageService.LoadCache<MiniMapSettings>("minimap_settings");
                if (settings != null)
                {
                    CmbShape.SelectedIndex = settings.ShapeIndex;
                    SliOpacity.Value = settings.Opacity;
                    ChkShowTime.IsChecked = settings.ShowTime;
                    ChkShowPop.IsChecked = settings.ShowPop;
                    SliSize.Value = settings.Size;
                    ApplyLayerChecks(settings);
                    ParentWindow?.ApplyLoadedSettings(settings);
                }
                else
                {
                    CmbShape.SelectedIndex = 0; // Default Circle
                    SliOpacity.Value = 1.0;
                    ChkShowTime.IsChecked = false;
                    ChkShowPop.IsChecked = false;
                    SliSize.Value = 260.0;
                    var defaults = new MiniMapSettings(0, 260.0, 1.0, false, false);
                    ApplyLayerChecks(defaults);
                    ParentWindow?.ApplyLoadedSettings(defaults);
                }

                // The dock layout is the window's, not part of MiniMapSettings — it is saved
                // and loaded with the tiles it describes. The zoom belongs to it for the same
                // reason: an arrangement is saved at a pitch, and carries it.
                CmbGrowth.SelectedIndex = ParentWindow?.DockGrowsRight == true ? 1 : 0;
                SliGridZoom.Value = ParentWindow?.DockZoom ?? 1.0;
                UpdateGridZoomLabel(SliGridZoom.Value);

                // Apply current labels
                UpdateOpacityLabel(SliOpacity.Value);
                UpdateSizeLabel(SliSize.Value);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public void UpdateSliderValue(double value)
        {
            _isInitializing = true;
            try
            {
                if (SliSize != null)
                {
                    SliSize.Value = value;
                    UpdateSizeLabel(value);
                }
            }
            finally
            {
                _isInitializing = false;
            }
            SaveSettings();
        }

        private void BtnSettingsClose_Click(object sender, RoutedEventArgs e)
            => ParentWindow?.CloseSettings();

        private void ApplyLayerChecks(MiniMapSettings settings)
        {
            ChkLayerTexture.IsChecked = settings.ShowTexture;
            ChkLayerGrid.IsChecked = settings.ShowGrid;
            ChkLayerDrawings.IsChecked = settings.ShowDrawings;
            ChkLayerIcons.IsChecked = settings.ShowIcons;
            ChkLayerPlayers.IsChecked = settings.ShowPlayers;
            ChkLayerDeaths.IsChecked = settings.ShowDeaths;
            ChkLayerHeatmap.IsChecked = settings.ShowHeatmap;
            ChkLayerNoBuild.IsChecked = settings.ShowNoBuild;
            RefreshNoBuildAvailability();
        }

        /// <summary>
        /// Enables the no-build layer only when this server's map has actually been parsed, and
        /// says why when it has not.
        ///
        /// Re-checked every time the panel is shown rather than once: a parse started from the
        /// main map finishes while this panel is closed, and the switch has to be live when it
        /// is opened again.
        /// </summary>
        public void RefreshNoBuildAvailability()
        {
            if (ChkLayerNoBuild == null) return;

            bool available = (Application.Current?.MainWindow as Views.MainWindow)?.NoBuildZonesAvailable == true;

            ChkLayerNoBuild.IsEnabled = available;
            ChkLayerNoBuild.Opacity = available ? 1.0 : 0.45;
            ChkLayerNoBuild.ToolTip = available
                ? null
                : Helpers.Loc.Text("MiniMapLayerNoBuildUnavailable", "Please parse the map first");

            // A layer that cannot be drawn must not stay ticked, or the dock would keep asking
            // the main map to build something that does not exist.
            if (!available && ChkLayerNoBuild.IsChecked == true)
            {
                bool was = _isInitializing;
                _isInitializing = true;
                try { ChkLayerNoBuild.IsChecked = false; }
                finally { _isInitializing = was; }
            }
        }

        private void CmbGrowth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ParentWindow == null) return;
            ParentWindow.DockGrowsRight = CmbGrowth.SelectedIndex == 1;
        }

        /// <summary>
        /// Switches every map layer back on. Called when the map is added back to the dock: it
        /// may well have been removed because it was invisible, and handing it back in that state
        /// would look like nothing happened.
        /// </summary>
        public void TurnAllLayersOn()
        {
            _isInitializing = true;
            try
            {
                ChkLayerTexture.IsChecked = true;
                ChkLayerGrid.IsChecked = true;
                ChkLayerDrawings.IsChecked = true;
                ChkLayerIcons.IsChecked = true;
                ChkLayerPlayers.IsChecked = true;
                ChkLayerDeaths.IsChecked = true;
                ChkLayerHeatmap.IsChecked = true;

                // No-build stays as it was, on purpose. It is the one layer that is not simply
                // a view of something the map always has: without a parse there is nothing to
                // switch on, and switching it on after one would be a choice nobody made.
            }
            finally
            {
                _isInitializing = false;
            }

            var settings = CurrentSettings();
            if (settings == null) return;

            ParentWindow?.ApplyLayerVisibility(settings);
            StorageService.SaveCache("minimap_settings", settings);
        }

        private void ChkLayer_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            var settings = CurrentSettings();
            if (settings == null) return;

            ParentWindow?.ApplyLayerVisibility(settings);
            StorageService.SaveCache("minimap_settings", settings);
        }

        private void CmbShape_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ParentWindow == null) return;

            // The window holds the shape and writes it through; this control only offers it.
            ParentWindow.SetMapShape(CmbShape.SelectedIndex);
        }

        /// <summary>
        /// Shows a shape the window was given from somewhere else - applying a saved arrangement,
        /// most of the time.
        ///
        /// Guarded, because assigning SelectedIndex raises SelectionChanged, which would hand the
        /// same shape straight back to the window it just came from.
        /// </summary>
        public void SyncShapeSelection(int shapeIndex)
        {
            if (CmbShape == null || CmbShape.SelectedIndex == shapeIndex) return;

            bool was = _isInitializing;
            _isInitializing = true;
            try { CmbShape.SelectedIndex = shapeIndex; }
            finally { _isInitializing = was; }
        }

        private void SliOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateOpacityLabel(e.NewValue);

            if (_isInitializing) return;

            if (ParentWindow != null && ParentWindow.MapShapeBorder != null)
            {
                ParentWindow.MapShapeBorder.Opacity = e.NewValue;
            }

            SaveSettings();
        }

        /// <summary>
        /// The grid's zoom. Lives on the window, like the shape does - this control shows it and
        /// writes through, so a second control elsewhere cannot disagree with it.
        /// </summary>
        private void SliGridZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateGridZoomLabel(e.NewValue);

            if (_isInitializing || ParentWindow == null) return;
            ParentWindow.SetGridZoom(e.NewValue);
        }

        /// <summary>Shows a zoom the window was given from somewhere else, without writing back.</summary>
        public void SyncGridZoom(double zoom)
        {
            if (SliGridZoom == null || Math.Abs(SliGridZoom.Value - zoom) < 0.001) return;

            bool was = _isInitializing;
            _isInitializing = true;
            try { SliGridZoom.Value = zoom; }
            finally { _isInitializing = was; }

            UpdateGridZoomLabel(zoom);
        }

        private void UpdateGridZoomLabel(double zoom)
        {
            if (LblGridZoom == null) return;

            string fmt = Helpers.Loc.Text("CommandDockGridZoomValue", "Widget zoom {0}%");
            LblGridZoom.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, fmt, (int)Math.Round(zoom * 100));
        }

        private void SliSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSizeLabel(e.NewValue);

            if (_isInitializing || ParentWindow == null) return;
            ParentWindow.UpdateSize(e.NewValue, updateSlider: false);

            // UpdateSize only saves on the path that writes the slider back — which is every
            // path except this one, where the slider is already where the user put it. So
            // dragging it changed the map and saved nothing, and the next load undid it.
            SaveSettings();
        }

        private void ChkShowTime_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (ParentWindow != null && ParentWindow.TimeOverlayBorder != null)
            {
                ParentWindow.TimeOverlayBorder.Visibility = (ChkShowTime.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            }

            SaveSettings();
        }

        private void ChkShowPop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            if (ParentWindow != null && ParentWindow.PopOverlayBorder != null)
            {
                ParentWindow.PopOverlayBorder.Visibility = (ChkShowPop.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
            }

            SaveSettings();
        }

        private void UpdateOpacityLabel(double opacityValue)
        {
            int pct = (int)Math.Round(opacityValue * 100);
            if (LblOpacity != null)
            {
                LblOpacity.Text = string.Format(Properties.Resources.OpacityLabel, pct);
            }
        }

        private void UpdateSizeLabel(double sizeValue)
        {
            int pct = (int)Math.Round((sizeValue / 260.0) * 100);
            if (LblSize != null)
            {
                LblSize.Text = string.Format(Properties.Resources.SizeLabel, pct);
            }
        }

        public void SaveSettings()
        {
            var settings = CurrentSettings();
            if (settings != null) StorageService.SaveCache("minimap_settings", settings);
        }

        private MiniMapSettings? CurrentSettings()
        {
            if (CmbShape == null || SliOpacity == null || SliSize == null || ChkShowTime == null) return null;

            return new MiniMapSettings(
                CmbShape.SelectedIndex,
                SliSize.Value,
                SliOpacity.Value,
                ChkShowTime.IsChecked == true,
                ChkShowPop.IsChecked == true,
                ChkLayerTexture?.IsChecked != false,
                ChkLayerGrid?.IsChecked != false,
                ChkLayerDrawings?.IsChecked != false,
                ChkLayerIcons?.IsChecked != false,
                ChkLayerPlayers?.IsChecked != false,
                ChkLayerDeaths?.IsChecked != false,
                ChkLayerHeatmap?.IsChecked != false,

                // Reads the opposite way round to the others: they default on when the control
                // is missing, this one defaults off, matching the record's own default.
                ChkLayerNoBuild?.IsChecked == true
            );
        }
    }
}
