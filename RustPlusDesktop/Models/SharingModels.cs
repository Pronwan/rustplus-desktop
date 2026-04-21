using System;
using System.Collections.Generic;
using System.Windows;

namespace RustPlusDesk.Models
{
    public class OverlaySaveData
    {
        public long LastUpdatedUnix { get; set; } = 0; // Unix seconds
        public List<SavedStroke> Strokes { get; set; } = new();
        public List<SavedIcon> Icons { get; set; } = new();
        public List<SavedText> Texts { get; set; } = new();
        public List<ExportedDeviceDto> Devices { get; set; } = new();
    }

    public sealed class ExportedDeviceDto
    {
        public uint EntityId { get; set; }
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public string? Alias { get; set; }
        public bool IsGroup { get; set; }
        public List<ExportedDeviceDto>? Children { get; set; }
    }

    public class SavedStroke
    {
        public List<Point> Points { get; set; } = new();
        public string Color { get; set; } = "#FF0000";
        public double Thickness { get; set; } = 2.0;
    }

    public class SavedIcon
    {
        public string IconPath { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 32;
        public double Height { get; set; } = 32;
        public string? Label { get; set; }
    }

    public class SavedText
    {
        public string Content { get; set; } = "";
        public string Color { get; set; } = "#FFFFFFFF";
        public double FontSize { get; set; } = 16.0;
        public double X { get; set; }
        public double Y { get; set; }
        public bool Bold { get; set; } = true;
    }
}
