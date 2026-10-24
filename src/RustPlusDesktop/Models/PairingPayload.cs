using System;

namespace RustPlusDesk.Models;


public class PairingPayload
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string? ServerId { get; set; } // Facepunch server GUID ("id" in the push); see ServerProfile.ServerId
    public string SteamId64 { get; set; } = "";
    public string PlayerToken { get; set; } = ""; // numerisch als String ok; wir parsen später zu int
    public string? ServerName { get; set; }
    public string? ServerDescription { get; set; }
    public string? ImageUrl { get; set; } // server logo (or banner) from the push
    public uint? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? EntityType { get; set; } // z.B. "server", "entity", "SmartSwitch" …
    public string? IssueDate { get; set; }
    public string? ExpiryDate { get; set; }

}


