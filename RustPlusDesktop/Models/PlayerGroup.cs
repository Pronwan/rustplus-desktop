using System;
using System.Collections.Generic;

namespace RustPlusDesk.Models;

public class PlayerGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#FF6B35";
    public bool NotifyOnOnline { get; set; } = false;
    public List<string> BMIds { get; set; } = new();
}
