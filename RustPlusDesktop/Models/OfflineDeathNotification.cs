using System;

namespace RustPlusDesk.Models
{
    public class OfflineDeathNotification
    {
        public DateTime Timestamp { get; set; }
        public string ServerName { get; set; } = string.Empty;
        public string AttackerName { get; set; } = string.Empty;

        public OfflineDeathNotification() { }

        public OfflineDeathNotification(DateTime timestamp, string serverName, string attackerName)
        {
            Timestamp = timestamp;
            ServerName = serverName;
            AttackerName = attackerName;
        }
    }
}
