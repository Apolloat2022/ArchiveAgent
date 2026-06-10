namespace ArchiveAgent.Core.Domain;

/// <summary>An immutable record of every agent decision — the audit trail.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public int RecordId { get; set; }
    public string Action { get; set; } = string.Empty;   // Archived | Kept | Review
    public string Detail { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public int? TokensUsed { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
