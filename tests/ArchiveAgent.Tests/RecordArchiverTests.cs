using ArchiveAgent.Core.Data;
using ArchiveAgent.Core.Domain;
using Xunit;

namespace ArchiveAgent.Tests;

/// <summary>
/// Verifies the stored-procedure -> Entity Framework migration (RecordArchiver) behaves
/// like the legacy proc: archive old, non-archived, non-PII rows; leave the rest untouched.
/// A parity test: the EF logic must match the behaviour of the original stored procedure.
/// </summary>
public class RecordArchiverTests
{
    [Fact]
    public async Task ArchivesOldNonPii_ExcludesPiiAndRecent()
    {
        using var t = new TestDb();
        var cutoff = DateTime.UtcNow.AddDays(-365);

        t.Db.Records.AddRange(
            new Record { ExternalId = "old-op",  Type = "Log",  Category = DataCategory.Operational, CreatedUtc = DateTime.UtcNow.AddDays(-800), Status = RecordStatus.Classified },
            new Record { ExternalId = "old-pii", Type = "Note", Category = DataCategory.PII,         CreatedUtc = DateTime.UtcNow.AddDays(-800), Status = RecordStatus.Classified },
            new Record { ExternalId = "recent",  Type = "Log",  Category = DataCategory.Operational, CreatedUtc = DateTime.UtcNow.AddDays(-10),  Status = RecordStatus.Classified }
        );
        t.Db.SaveChanges();

        var updated = await new RecordArchiver(t.Db).ArchiveOldRecordsAsync(cutoff);

        // ExecuteUpdate bypasses the change tracker — clear it so assertions re-read from the DB.
        t.Db.ChangeTracker.Clear();

        Assert.Equal(1, updated);
        Assert.Single(t.Db.ArchivedRecords);
        Assert.Equal("old-op", t.Db.ArchivedRecords.Single().ExternalId);

        Assert.Equal(RecordStatus.Archived,   t.Db.Records.Single(r => r.ExternalId == "old-op").Status);
        Assert.Equal(RecordStatus.Classified, t.Db.Records.Single(r => r.ExternalId == "old-pii").Status); // PII untouched
        Assert.Equal(RecordStatus.Classified, t.Db.Records.Single(r => r.ExternalId == "recent").Status);  // recent untouched
    }

    [Fact]
    public async Task RunningTwice_IsIdempotent()
    {
        using var t = new TestDb();
        var cutoff = DateTime.UtcNow.AddDays(-365);
        t.Db.Records.Add(new Record { ExternalId = "old-op", Type = "Log", Category = DataCategory.Operational, CreatedUtc = DateTime.UtcNow.AddDays(-800), Status = RecordStatus.Classified });
        t.Db.SaveChanges();

        var archiver = new RecordArchiver(t.Db);
        var first = await archiver.ArchiveOldRecordsAsync(cutoff);
        t.Db.ChangeTracker.Clear();
        var second = await archiver.ArchiveOldRecordsAsync(cutoff);
        t.Db.ChangeTracker.Clear();

        Assert.Equal(1, first);
        Assert.Equal(0, second); // already Archived -> excluded the second time
        Assert.Single(t.Db.ArchivedRecords);
    }
}
