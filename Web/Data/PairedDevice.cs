namespace RustPlusWeb.Data;

/// <summary>Metadata for a Smart Switch, Smart Alarm, or Storage Monitor paired through Rust+.</summary>
public sealed class PairedDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PairedServerCredentialId { get; set; }
    public string EntityId { get; set; } = "";
    public Guid? GroupId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
