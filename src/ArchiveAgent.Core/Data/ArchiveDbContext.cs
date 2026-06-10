using ArchiveAgent.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArchiveAgent.Core.Data;

public class ArchiveDbContext : DbContext
{
    public ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : base(options) { }

    public DbSet<Record> Records => Set<Record>();
    public DbSet<ArchivedRecord> ArchivedRecords => Set<ArchivedRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ReviewItem> ReviewItems => Set<ReviewItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Record>(e =>
        {
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedUtc);
            e.Property(x => x.ExternalId).HasMaxLength(100);
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RetentionClass).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<ArchivedRecord>(e =>
        {
            e.HasIndex(x => x.OriginalRecordId);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RetentionClass).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<ReviewItem>(e => e.HasIndex(x => x.Resolved));
    }
}
