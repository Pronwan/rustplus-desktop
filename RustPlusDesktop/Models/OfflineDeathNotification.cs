using System;

namespace RustPlusDesk.Models
{
    public class OfflineDeathNotification
    {
        public DateTime Timestamp { get; set; }
        public string ServerName { get; set; } = string.Empty;
        public string AttackerName { get; set; } = string.Empty;
        public string? Ip { get; set; } = null;
        public int? Port { get; set; } = null;

        public OfflineDeathNotification() { }

        public OfflineDeathNotification(DateTime timestamp, string serverName, string attackerName, string? ip = null, int? port = null)
        {
            Timestamp = timestamp;
            ServerName = serverName;
            AttackerName = attackerName;
            Ip = ip;
            Port = port;
        }
    }
}
