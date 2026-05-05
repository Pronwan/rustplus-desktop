using System;
using System.Collections.Generic;

namespace RustPlusDesk.Models;

public class GroupMapPin
{
    /// <summary>Server name as reported by the app (e.g. "Rustafied.com - AU Main").</summary>
    public string Server { get; set; } = "";
    /// <summary>World-coordinate X (game units), as captured via ImagePxToWorld at click time.</summary>
    public double X { get; set; }
    /// <summary>World-coordinate Y (game units).</summary>
    public double Y { get; set; }
}

public class PlayerGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#FF6B35";
    public bool NotifyOnOnline { get; set; } = false;
    public List<string> BMIds { get; set; } = new();
    /// <summary>Per-server pinned base location. At most one pin per (group, server).</summary>
    public List<GroupMapPin> MapPins { get; set; } = new();
}
