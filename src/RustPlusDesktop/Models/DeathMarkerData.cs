using System;

namespace RustPlusDesk.Models
{
    public class DeathMarkerData
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ulong SteamId { get; set; }
        public string OriginalName { get; set; } = string.Empty;
        public string CustomName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public DateTime TimeOfDeath { get; set; }
    }
}
