using ArchiveAgent.Core.Agents;
using ArchiveAgent.Core.Ai;
using ArchiveAgent.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchiveAgent.Tests;

/// <summary>
/// End-to-end tests of the agent loop against a real (SQLite) DB with a fake classifier.
/// These prove the production-critical behavior: the verification gate, not the happy path.
/// </summary>
public class ArchiveAgentServiceTests
{
    private static ClassificationService Classifier(string json) =>
        new(new FakeClaudeClient(json), NullLogger<ClassificationService>.Instance);

    private static ArchiveAgentService Agent(TestDb t, string json) =>
        new(t.Db, Classifier(json), NullLogger<ArchiveAgentService>.Instance);

    private const string Disposable = """{ "category":"Operational","retentionClass":"Disposable","confidence":0.95,"reason":"routine log" }""";

    [Fact]
    public async Task OldDisposableRecord_IsArchived()
    {
        using var t = new TestDb();
        t.Db.Records.Add(new Record { ExternalId = "A", Type = "Log", CreatedUtc = DateTime.UtcNow.AddDays(-800), Status = RecordStatus.Pending });
        t.Db.SaveChanges();

        var summary = await Agent(t, Disposable).RunAsync();

        Assert.Equal(1, summary.Archived);
        Assert.Single(t.Db.ArchivedRecords);
        Assert.Equal(RecordStatus.Archived, t.Db.Records.Single().Status);
        Assert.NotEmpty(t.Db.AuditLogs);
    }

    [Fact]
    public async Task PiiRecord_IsNeverArchived_AndGoesToReview()
    {
        using var t = new TestDb();
        t.Db.Records.Add(new Record { ExternalId = "B", Type = "CustomerNote", CreatedUtc = DateTime.UtcNow.AddDays(-800), Status = RecordStatus.Pending });
        t.Db.SaveChanges();

        const string pii = """{ "category":"PII","retentionClass":"OneYear","confidence":0.95,"reason":"contains name and phone" }""";
        var summary = await Agent(t, pii).RunAsync();

        Assert.Equal(0, summary.Archived);
        Assert.Equal(1, summary.Review);
        Assert.Empty(t.Db.ArchivedRecords);
        Assert.Equal(RecordStatus.NeedsReview, t.Db.Records.Single().Status);
        Assert.Single(t.Db.ReviewItems);
    }

    [Fact]
    public async Task LowConfidence_GoesToReview_NotArchived()
    {
        using var t = new TestDb();
        t.Db.Records.Add(new Record { ExternalId = "C", Type = "Log", CreatedUtc = DateTime.UtcNow.AddDays(-800), Status = RecordStatus.Pending });
        t.Db.SaveChanges();

        const string lowConf = """{ "category":"Operational","retentionClass":"Disposable","confidence":0.40,"reason":"unsure" }""";
        var summary = await Agent(t, lowConf).RunAsync();

        Assert.Equal(0, summary.Archived);
        Assert.Equal(1, summary.Review);
        Assert.Equal(RecordStatus.NeedsReview, t.Db.Records.Single().Status);
    }

    [Fact]
    public async Task RecentRecord_BlockedByVerificationGate_GoesToReview()
    {
        using var t = new TestDb();
        t.Db.Records.Add(new Record { ExternalId = "D", Type = "Log", CreatedUtc = DateTime.UtcNow.AddDays(-10), Status = RecordStatus.Pending });
        t.Db.SaveChanges();

        var summary = await Agent(t, Disposable).RunAsync(); // confident archive, but too young

        Assert.Equal(0, summary.Archived);
        Assert.Equal(1, summary.Review);
    }

    [Fact]
    public async Task UnparseableModelOutput_DoesNotThrow_AndEscalates()
    {
        using var t = new TestDb();
        t.Db.Records.Add(new Record { ExternalId = "E", Type = "Log", CreatedUtc = DateTime.UtcNow.AddDays(-800), Status = RecordStatus.Pending });
        t.Db.SaveChanges();

        var summary = await Agent(t, "Sorry, I can't do that.").RunAsync(); // not JSON

        Assert.Equal(0, summary.Archived);
        Assert.Equal(1, summary.Review); // low-confidence fallback routes to human review
    }
}
