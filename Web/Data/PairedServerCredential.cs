namespace RustPlusWeb.Data;

/// <summary>
/// A server paired through the administrator's Rust+ FCM account.
/// The player token is protected with ASP.NET Data Protection before it is persisted.
/// </summary>
public sealed class PairedServerCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = "";
    public Guid RustServerId { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string PlayerId { get; set; } = "";
    public string ProtectedPlayerToken { get; set; } = "";
    public bool ConnectionEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
