using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();
    public DbSet<Tunnel> Tunnels => Set<Tunnel>();
    public DbSet<AdminSettings> AdminSettings => Set<AdminSettings>();
    public DbSet<IssuedCertificate> IssuedCertificates => Set<IssuedCertificate>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ApiKey>(e =>
        {
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasOne(x => x.Owner)
                .WithMany(u => u.ApiKeys)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InviteCode>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<IssuedCertificate>(e =>
        {
            e.HasIndex(x => x.Host).IsUnique();
        });

        b.Entity<AuditEvent>(e =>
        {
            // The viewer orders/filters by time and type; index both.
            e.HasIndex(x => x.CreatedAtUnixMs);
            e.HasIndex(x => x.EventType);
        });

        b.Entity<Tunnel>(e =>
        {
            e.HasIndex(x => x.Subdomain);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.ApiKey)
                .WithMany(k => k.Tunnels)
                .HasForeignKey(x => x.ApiKeyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
