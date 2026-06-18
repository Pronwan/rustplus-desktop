using System;

namespace RustPlusDesk.Models
{
    public class RustPlusNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Type { get; set; } = string.Empty; // "Alarm", "Death", "Chat", "Pairing"
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ServerIp { get; set; }
        public int? ServerPort { get; set; }
        public string? ServerName { get; set; }
        public bool IsRead { get; set; } = false;

        // Type-specific extra details (optional)
        public uint? EntityId { get; set; }
        public string? EntityName { get; set; }
        public string? AttackerName { get; set; }
        public string? ChatAuthor { get; set; }

        // FCM message id used for persistent de-duplication across listener restarts
        public string? FcmNotificationId { get; set; }

        public RustPlusNotification() { }

        public RustPlusNotification(string type, string title, string message, string? serverIp = null, int? serverPort = null, string? serverName = null)
        {
            Type = type;
            Title = title;
            Message = message;
            ServerIp = serverIp;
            ServerPort = serverPort;
            ServerName = serverName;
        }
    }
}
