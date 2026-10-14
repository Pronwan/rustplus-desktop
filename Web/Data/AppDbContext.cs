using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RustPlusWeb.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ServerProfile> ServerProfiles => Set<ServerProfile>();
    public DbSet<PairedServerCredential> PairedServerCredentials => Set<PairedServerCredential>();
    public DbSet<PairedDevice> PairedDevices => Set<PairedDevice>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ServerProfile>()
            .HasIndex(profile => new { profile.OwnerUserId, profile.Name })
            .IsUnique();
        builder.Entity<ServerProfile>()
            .Property(profile => profile.Name).HasMaxLength(80);
        builder.Entity<ServerProfile>()
            .Property(profile => profile.Host).HasMaxLength(255);
        builder.Entity<PairedServerCredential>()
            .HasIndex(profile => profile.RustServerId).IsUnique();
        builder.Entity<PairedServerCredential>()
            .Property(profile => profile.Name).HasMaxLength(120);
        builder.Entity<PairedServerCredential>()
            .Property(profile => profile.Host).HasMaxLength(255);
        builder.Entity<PairedDevice>()
            .HasIndex(device => new { device.PairedServerCredentialId, device.EntityId }).IsUnique();
        builder.Entity<PairedDevice>()
            .Property(device => device.Name).HasMaxLength(120);
        builder.Entity<PairedDevice>()
            .Property(device => device.Type).HasMaxLength(32);
    }
}

public sealed class ServerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 28082;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
