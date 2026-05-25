using System;
using System.Collections.Generic;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services
{
    public class CustomCrosshair
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "CUSTOM";
        public string Base64Image { get; set; } = "";
    }

    public static class CustomCrosshairManager
    {
        public static List<CustomCrosshair> LoadCrosshairs()
        {
            return CrosshairDataModule.LoadCrosshairs();
        }

        public static void SaveCrosshairs(List<CustomCrosshair> crosshairs)
        {
            CrosshairDataModule.SaveCrosshairs(crosshairs);
        }
    }
}
