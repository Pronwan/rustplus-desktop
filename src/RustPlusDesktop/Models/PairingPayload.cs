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
    public string? NotificationId { get; set; }
    public DateTime? Timestamp { get; set; }

    /// <summary>
    /// Replayed from Facepunch's notification history rather than pushed just now.
    /// Its <see cref="Timestamp"/> is when it was really paired, which may be long ago.
    /// </summary>
    public bool FromHistory { get; set; }

    /// <summary>
    /// The user confirmed bringing back a device they had deleted; the deletion record
    /// must not block this pairing.
    /// </summary>
    public bool UserConfirmedRestore { get; set; }

    /// <summary>When this was paired, in UTC; "now" when the push carried no time.</summary>
    public DateTime PairedAtUtc => Timestamp?.ToUniversalTime() ?? DateTime.UtcNow;

}


