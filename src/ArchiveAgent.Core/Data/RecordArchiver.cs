using ArchiveAgent.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArchiveAgent.Core.Data;

/// <summary>
/// Set-based Entity Framework reimplementation of the legacy dbo.sp_ArchiveOldRecords
/// stored procedure: testable C# that keeps the same set-based semantics.
/// See Legacy/sp_ArchiveOldRecords.sql and Legacy/MIGRATION.md.
/// </summary>
public class RecordArchiver
{
    private readonly ArchiveDbContext _db;

    public RecordArchiver(ArchiveDbContext db) => _db = db;

    /// <summary>Archive all records older than the cutoff that are not already archived and not PII.</summary>
    /// <returns>Number of records archived.</returns>
    public async Task<int> ArchiveOldRecordsAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Project matching rows and bulk-insert into the archive (no entities tracked).
        var toArchive = await _db.Records
            .Where(r => r.CreatedUtc < cutoffUtc
                     && r.Status != RecordStatus.Archived
                     && r.Category != DataCategory.PII)
            .Select(r => new ArchivedRecord
            {
                OriginalRecordId = r.Id,
                ExternalId = r.ExternalId,
                Type = r.Type,
                Title = r.Title,
                Content = r.Content,
                CreatedUtc = r.CreatedUtc,
                Category = r.Category,
                RetentionClass = r.RetentionClass,
                ArchivedUtc = DateTime.UtcNow,
                ArchiveReason = "EF migration: past cutoff"
            })
            .ToListAsync(ct);

        _db.ArchivedRecords.AddRange(toArchive);

        // 2. Set-based status update (EF Core 7+ ExecuteUpdate — no loop, no entities loaded).
        var updated = await _db.Records
            .Where(r => r.CreatedUtc < cutoffUtc
                     && r.Status != RecordStatus.Archived
                     && r.Category != DataCategory.PII)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecordStatus.Archived), ct);

        await _db.SaveChangesAsync(ct); // persists the archive inserts
        await tx.CommitAsync(ct);
        return updated;
    }
}
