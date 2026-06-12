using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustPlusDesk.Models
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

    [System.Flags]
    public enum CameraButtons
    {
        None = 0,
        Forward = 2,
        Backward = 4,
        Left = 8,
        Right = 16,
        Jump = 32,
        Duck = 64,
        Sprint = 128,
        Use = 256,
        FirePrimary = 1024,
        FireSecondary = 2048,
        Reload = 8192,
        FireThird = 134217728
    }

    [System.Flags]
    public enum CameraControlFlags
    {
        None = 0,
        Movement = 1,
        Mouse = 2,
        SprintAndDuck = 4,
        Fire = 8,
        Reload = 16,
        Crosshair = 32
    }
}

