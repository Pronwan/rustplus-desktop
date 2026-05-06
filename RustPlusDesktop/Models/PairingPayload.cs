using System;

namespace RustPlusDesk.Models;


public class PairingPayload
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string SteamId64 { get; set; } = "";
    public string PlayerToken { get; set; } = ""; // numerisch als String ok; wir parsen später zu int
    public string? ServerName { get; set; }
    public uint? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? EntityType { get; set; } // z.B. "server", "entity", "SmartSwitch" …

}


