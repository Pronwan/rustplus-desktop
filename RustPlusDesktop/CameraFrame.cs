using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustPlusDesk.Views
{
    public sealed class CameraEntity
    {
        public int EntityId { get; }
        public int Type { get; }
        public ulong SteamId { get; }   // <- ulong
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public string Label { get; }

        public CameraEntity(double x, double y, double z, string label, int entityId = 0, int type = 0, ulong steamId = 0)
        {
            X = x; Y = y; Z = z;
            Label = label ?? "";
            EntityId = entityId;
            Type = type;
            SteamId = steamId;
        }
    }

    // Einheitlicher Frame-Typ mit optionalen Extras (Mime, Zeitstempel, Entities)
    public sealed record CameraFrame(
    byte[] Bytes,
    string? Mime,
    int Width,
    int Height,
    IReadOnlyList<CameraEntity> Entities
);
}

