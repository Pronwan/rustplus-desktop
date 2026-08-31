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

        /// <summary>
        /// The server wipe these entity IDs belong to, in Unix seconds. Zero for snapshots
        /// written before this was recorded.
        ///
        /// Rust hands out a fresh net ID to every deployable on a wipe, so a snapshot from an
        /// earlier one lists devices that no longer exist. The server key is ip-port and does
        /// not change across a wipe, so nothing else distinguishes the two — the devices import
        /// happily and then sit there red, which is what sent us looking for a bug in sharing.
        /// </summary>
        public long WipeTimeUnix { get; set; } = 0;
        public List<SavedStroke> Strokes { get; set; } = new();
        public List<SavedIcon> Icons { get; set; } = new();
        public List<SavedText> Texts { get; set; } = new();
        public List<ExportedDeviceDto> Devices { get; set; } = new();

        /// <summary>
        /// Named routes, with their own geometry rather than as strokes.
        ///
        /// A route is a thing you name, colour, hide and measure - none of which a stroke has a
        /// place for. Older builds simply do not see this property and carry on with the strokes,
        /// and a payload written by one of them arrives here with no routes, which is correct.
        /// </summary>
        public List<SavedRoute> Routes { get; set; } = new();

        /// <summary>
        /// Ids of teammates' routes that have already been copied into this list.
        ///
        /// Without it, deleting an imported route would only last until the next sync brought it
        /// straight back — which is not a delete, it is a flicker.
        /// </summary>
        public List<string> ImportedRouteIds { get; set; } = new();
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

        /// <summary>In-game alarm text. The cloud worker matches pushes against this.</summary>
        public string? InGameAlarmTitle { get; set; }

        /// <summary>"SmallOilRig"/"LargeOilRig" when a rule uses this alarm as a rig trigger.
        /// Lets the cloud worker tell a crate hack from a raid while the app is closed.</summary>
        public string? OilRigTrigger { get; set; }
    }

    [JsonConverter(typeof(SavedStrokeJsonConverter))]
    public class SavedStroke
    {
        public List<Point> Points { get; set; } = new();
        public string Color { get; set; } = "#FF0000";
        public double Thickness { get; set; } = 2.0;
        /// <summary>Strokes sharing this id form one group/layer (e.g. an arrow route). Null when ungrouped.</summary>
        public string? GroupId { get; set; }
    }

    /// <summary>One named route: where it goes, what it is called, and whether it is shown.</summary>
    public class SavedRoute
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Color { get; set; } = "#FF3FD7FF";

        public double Thickness { get; set; } = 3.0;

        public List<Point> Points { get; set; } = new();

        /// <summary>
        /// The end has been walked back to the start, so the route is a lap. Stored rather than
        /// derived from the last point: two points can sit on top of each other by accident, and
        /// a lap is a decision rather than a coincidence.
        /// </summary>
        public bool Closed { get; set; }

        /// <summary>Hidden routes stay in the list and off the map.</summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// The id of the teammate's route this was copied from, or null when it is your own.
        ///
        /// Two jobs. It stops the same route being imported twice, and it keeps copies out of
        /// what gets shared: a copy that travels back out is a copy somebody else re-imports,
        /// which is how two clients grew each other a list of a hundred routes.
        /// </summary>
        public string? SourceId { get; set; }
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
                    else if (propertyName.Equals("GroupId", StringComparison.OrdinalIgnoreCase) ||
                             propertyName.Equals("g", StringComparison.OrdinalIgnoreCase))
                    {
                        stroke.GroupId = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
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
            if (!string.IsNullOrEmpty(value.GroupId))
                writer.WriteString("g", value.GroupId);
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
