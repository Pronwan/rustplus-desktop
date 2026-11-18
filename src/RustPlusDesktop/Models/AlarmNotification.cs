using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RustPlusDesk.Models;

    

    public record AlarmNotification(
        DateTime Timestamp,
        string Server,
        string DeviceName,
        uint? EntityId,
        string Message,
        string? Ip = null,
        int? Port = null,
        string? Title = null,
        string? FcmNotificationId = null,
        DateTime? EventTime = null,
        string? ImageUrl = null,
        // A modded server's own alert: on the alarm channel, but without the typed body a Smart
        // Alarm's push carries ("Wall destroyed at R9" from a plugin). The cloud tells them apart
        // the same way.
        bool IsServerAlert = false
    );

