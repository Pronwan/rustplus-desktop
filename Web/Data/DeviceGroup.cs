namespace RustPlusWeb.Data;

/// <summary>A shared, server-scoped group for paired Rust+ devices.</summary>
public sealed class DeviceGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PairedServerCredentialId { get; set; }
    public string Name { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
