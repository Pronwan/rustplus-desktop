using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        public int? CustomIconId { get; set; }
        public string? CustomIconShortName { get; set; }
    }

    [JsonConverter(typeof(SavedStrokeJsonConverter))]
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
        public string? Note { get; set; }
        public List<string>? Screenshots { get; set; } // List of Base64 compressed images
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

    public class SavedStrokeJsonConverter : JsonConverter<SavedStroke>
    {
        public override SavedStroke Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var stroke = new SavedStroke();
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return stroke;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string propertyName = reader.GetString() ?? "";
                    reader.Read();

                    if (propertyName.Equals("Color", StringComparison.OrdinalIgnoreCase))
                    {
                        stroke.Color = reader.GetString() ?? "#FF0000";
                    }
                    else if (propertyName.Equals("Thickness", StringComparison.OrdinalIgnoreCase))
                    {
                        stroke.Thickness = reader.GetDouble();
                    }
                    else if (propertyName.Equals("p", StringComparison.OrdinalIgnoreCase) ||
                             propertyName.Equals("Points", StringComparison.OrdinalIgnoreCase) ||
                             propertyName.Equals("points", StringComparison.OrdinalIgnoreCase))
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            stroke.Points = PolylineEncoder.Decode(reader.GetString() ?? "");
                        }
                        else if (reader.TokenType == JsonTokenType.StartArray)
                        {
                            var pts = new List<Point>();
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                if (reader.TokenType == JsonTokenType.StartObject)
                                {
                                    double px = 0;
                                    double py = 0;
                                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                                    {
                                        if (reader.TokenType == JsonTokenType.PropertyName)
                                        {
                                            string pKey = reader.GetString() ?? "";
                                            reader.Read();
                                            if (pKey.Equals("X", StringComparison.OrdinalIgnoreCase))
                                                px = reader.GetDouble();
                                            else if (pKey.Equals("Y", StringComparison.OrdinalIgnoreCase))
                                                py = reader.GetDouble();
                                        }
                                    }
                                    pts.Add(new Point(px, py));
                                }
                            }
                            stroke.Points = pts;
                        }
                    }
                }
            }
            return stroke;
        }

        public override void Write(Utf8JsonWriter writer, SavedStroke value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("Color", value.Color);
            writer.WriteNumber("Thickness", value.Thickness);
            writer.WriteString("p", PolylineEncoder.Encode(value.Points));
            writer.WriteEndObject();
        }
    }

    public static class PolylineEncoder
    {
        public static string Encode(List<Point> points)
        {
            if (points == null || points.Count == 0)
                return string.Empty;

            var str = new StringBuilder();
            int lastLat = 0;
            int lastLng = 0;

            foreach (var point in points)
            {
                int lat = (int)Math.Round(point.X * 1E5);
                int lng = (int)Math.Round(point.Y * 1E5);

                EncodeDiff(str, lat - lastLat);
                EncodeDiff(str, lng - lastLng);

                lastLat = lat;
                lastLng = lng;
            }

            return str.ToString();
        }

        public static List<Point> Decode(string encoded)
        {
            var points = new List<Point>();
            if (string.IsNullOrEmpty(encoded))
                return points;

            int index = 0;
            int len = encoded.Length;
            int lat = 0;
            int lng = 0;

            while (index < len)
            {
                int deltaLat = DecodeValue(encoded, ref index);
                int deltaLng = DecodeValue(encoded, ref index);

                lat += deltaLat;
                lng += deltaLng;

                points.Add(new Point(lat / 100000.0, lng / 100000.0));
            }

            return points;
        }

        private static void EncodeDiff(StringBuilder str, int diff)
        {
            int shifted = diff << 1;
            if (diff < 0) shifted = ~shifted;

            int rem = shifted;
            while (rem >= 0x20)
            {
                str.Append((char)((0x20 | (rem & 0x1f)) + 63));
                rem >>= 5;
            }
            str.Append((char)(rem + 63));
        }

        private static int DecodeValue(string encoded, ref int index)
        {
            int result = 0;
            int shift = 0;
            int b;
            do
            {
                if (index >= encoded.Length)
                    break;
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);

            return ((result & 1) != 0) ? ~(result >> 1) : (result >> 1);
        }
    }
}
