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
                    SliSize.Value = settings.Size;
                    ParentWindow?.ApplyLoadedSettings(settings);
                }
                else
                {
                    CmbShape.SelectedIndex = 0; // Default Circle
                    SliOpacity.Value = 1.0;
                    ChkShowTime.IsChecked = false;
                    SliSize.Value = 260.0;
                    ParentWindow?.ApplyLoadedSettings(new MiniMapSettings(0, 260.0, 1.0, false));
                }

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
        {
            Visibility = Visibility.Collapsed;
            if (ParentWindow != null)
            {
                ParentWindow.SettingsHoverBorder.Visibility = Visibility.Visible;
            }
        }

        private void CmbShape_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ParentWindow == null) return;
            ParentWindow.UpdateSize(ParentWindow.Width, updateSlider: true);
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
            if (CmbShape == null || SliOpacity == null || SliSize == null || ChkShowTime == null) return;

            var settings = new MiniMapSettings(
                CmbShape.SelectedIndex,
                SliSize.Value,
                SliOpacity.Value,
                ChkShowTime.IsChecked == true
            );

            StorageService.SaveCache("minimap_settings", settings);
        }
    }
}
