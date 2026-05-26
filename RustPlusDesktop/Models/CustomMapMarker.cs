using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Models;

public class CustomMapMarker
{
    [JsonPropertyName("type")]
    public string Name { get; set; } = "";

    [JsonPropertyName("coordinates")]
    public MarkerCoordinates Coordinates { get; set; } = new();

    [JsonPropertyName("sizeCategory")]
    public string? SizeCategory { get; set; }

    [JsonPropertyName("iconPath")]
    public string? IconPath { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; } = 14;

    public double X => Coordinates.X;
    public double Y => Coordinates.Y;
}

public class MarkerCoordinates
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

public class CustomMarkerFile
{
    [JsonPropertyName("monuments")]
    public List<CustomMapMarker> Markers { get; set; } = new();
}