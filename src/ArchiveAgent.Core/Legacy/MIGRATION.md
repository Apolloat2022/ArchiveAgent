# Stored Procedure → Entity Framework

How the legacy `sp_ArchiveOldRecords` stored procedure is converted into testable EF Core
logic, kept **set-based** (no row-by-row loop).

## The legacy proc
`sp_ArchiveOldRecords` (see `sp_ArchiveOldRecords.sql`) does two set operations in a transaction:
1. INSERT matching rows into `ArchivedRecords`
2. UPDATE those rows' status to `Archived`
Filter: older than cutoff, not already archived, not PII.

## EF Core equivalent (set-based, transactional)

```csharp
public async Task<int> ArchiveOldRecordsAsync(DateTime cutoffUtc, CancellationToken ct = default)
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    // 1. Project the matching rows and bulk-insert into the archive.
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

    // 2. Set-based status update (EF Core 7+ ExecuteUpdate — no entities loaded, no loop).
    var updated = await _db.Records
        .Where(r => r.CreatedUtc < cutoffUtc
                 && r.Status != RecordStatus.Archived
                 && r.Category != DataCategory.PII)
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RecordStatus.Archived), ct);

    await _db.SaveChangesAsync(ct);   // persists the inserts
    await tx.CommitAsync(ct);
    return updated;
}
```

## Notes & rationale
- **Why EF over the proc:** testable in C#, type-safe, no string SQL, same set-based performance via `ExecuteUpdate`/`ExecuteDelete`.
- **Parity test:** seed a fixture, run the proc on a copy and the EF method on another, assert identical `ArchivedRecords` + statuses (see `RecordArchiverTests`).
- **For millions of rows:** batch by id range or date window; consider SQL Server partition switching for true bulk archive.
- **Bridge during migration:** `FromSqlRaw("EXEC dbo.sp_ArchiveOldRecords @CutoffUtc={0}", cutoff)` lets you call the proc from EF while migrating incrementally.
- **AI angle:** the agent (`ArchiveAgentService`) replaces the blunt date filter with Claude-driven classification plus deterministic verification — AI-driven handling of database processes.
