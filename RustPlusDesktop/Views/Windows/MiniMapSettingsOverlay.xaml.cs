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

        private void MiniMapSettingsOverlay_Loaded(object sender, RoutedEventArgs e)
        {
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
                // and loaded with the tiles it describes.
                CmbGrowth.SelectedIndex = ParentWindow?.DockGrowsRight == true ? 1 : 0;

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

            // The slider is the map size; the window's own Width is the whole dock, which since
            // the command dock arrived is a different number entirely.
            ParentWindow.UpdateSize(SliSize.Value, updateSlider: false);
            SaveSettings();
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

        private void SliSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSizeLabel(e.NewValue);

            if (_isInitializing || ParentWindow == null) return;
            ParentWindow.UpdateSize(e.NewValue, updateSlider: false);
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
                ChkLayerPlayers?.IsChecked != false
            );
        }
    }
}
