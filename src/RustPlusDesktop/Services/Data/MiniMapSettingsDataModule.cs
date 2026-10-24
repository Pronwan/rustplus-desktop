using System;
using RustPlusDesk.Services;

namespace RustPlusDesk.Services.Data
{
    public static class MiniMapSettingsDataModule
    {
        public static void SaveSettings(MiniMapSettings settings)
        {
            DataManager.SaveCache("minimap_settings", settings);
        }

        public static MiniMapSettings? LoadSettings()
        {
            return DataManager.LoadCache<MiniMapSettings>("minimap_settings");
        }
    }
}
