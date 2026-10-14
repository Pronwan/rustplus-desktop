namespace RustPlusWeb.Data;

/// <summary>A durable, server-scoped event timer shared with signed-in teammates.</summary>
public sealed class ServerTimer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PairedServerCredentialId { get; set; }
    public string OwnerUserId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Fired { get; set; }
}
