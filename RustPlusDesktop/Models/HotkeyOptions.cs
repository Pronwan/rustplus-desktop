namespace RustPlusDesk.Models;

public sealed class HotkeyOptions
{
    public bool ParallelMode { get; set; } = false;
    public int ToggleDelayMs { get; set; } = 150;
}
