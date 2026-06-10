using ArchiveAgent.Core.Ai;
using ArchiveAgent.Core.Data;
using ArchiveAgent.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ArchiveAgent.Core.Agents;

/// <summary>
/// The agentic loop:
/// ingest -> classify (Claude) -> decide (rules) -> verify (gate) -> act (archive) -> log -> escalate.
/// </summary>
public class ArchiveAgentService
{
    private readonly ArchiveDbContext _db;
    private readonly ClassificationService _classifier;
    private readonly ILogger<ArchiveAgentService> _log;

    public ArchiveAgentService(ArchiveDbContext db, ClassificationService classifier, ILogger<ArchiveAgentService> log)
    {
        _db = db;
        _classifier = classifier;
        _log = log;
    }

    public async Task<RunSummary> RunAsync(int batchSize = 25, CancellationToken ct = default)
    {
        var summary = new RunSummary();

        var pending = await _db.Records
            .Where(r => r.Status == RecordStatus.Pending)
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var r in pending)
        {
            // 1. CLASSIFY
            var (cls, tokens) = await _classifier.ClassifyAsync(r, ct);
            var category = Enum.TryParse<DataCategory>(cls.Category, out var c) ? c : DataCategory.Unknown;
            var retention = Enum.TryParse<RetentionClass>(cls.RetentionClass, out var rc) ? rc : RetentionClass.Unknown;

            r.Category = category;
            r.RetentionClass = retention;
            r.Confidence = cls.Confidence;
            r.ClassificationReason = cls.Reason;

            // 2. DECIDE
            var action = RetentionRules.Decide(r, category, retention, cls.Confidence);

            // 3. VERIFY + 4. ACT / 6. ESCALATE
            if (action == AgentAction.Archive && RetentionRules.VerifyArchive(r, category, out var verify))
            {
                await ArchiveAsync(r, $"{cls.Reason} | {verify}", ct);
                summary.Archived++;
                AddAudit(r, "Archived", cls.Reason, cls.Confidence, tokens);
            }
            else if (action == AgentAction.Review || action == AgentAction.Archive) // archive that failed verification -> review
            {
                r.Status = RecordStatus.NeedsReview;
                _db.ReviewItems.Add(new ReviewItem { RecordId = r.Id, Reason = cls.Reason });
                summary.Review++;
                AddAudit(r, "Review", cls.Reason, cls.Confidence, tokens);
            }
            else
            {
                r.Status = RecordStatus.Classified;
                summary.Kept++;
                AddAudit(r, "Kept", cls.Reason, cls.Confidence, tokens);
            }

            summary.TokensUsed += tokens;
        }

        await _db.SaveChangesAsync(ct);
        summary.Processed = pending.Count;
        _log.LogInformation("Run complete: {@Summary}", summary);
        return summary;
    }

    /// <summary>Transactional move from the hot table to the archive (reversible, audited).</summary>
    private async Task ArchiveAsync(Record r, string reason, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        _db.ArchivedRecords.Add(new ArchivedRecord
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
            ArchiveReason = reason
        });
        r.Status = RecordStatus.Archived;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private void AddAudit(Record r, string action, string detail, double confidence, int tokens) =>
        _db.AuditLogs.Add(new AuditLog
        {
            RecordId = r.Id,
            Action = action,
            Detail = detail,
            Confidence = confidence,
            TokensUsed = tokens
        });
}

public class RunSummary
{
    public int Processed { get; set; }
    public int Archived { get; set; }
    public int Kept { get; set; }
    public int Review { get; set; }
    public int TokensUsed { get; set; }
}
