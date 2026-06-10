namespace ArchiveAgent.Core.Domain;

/// <summary>A record that has been moved out of the hot table into the archive.</summary>
public class ArchivedRecord
{
    public int Id { get; set; }
    public int OriginalRecordId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DataCategory Category { get; set; }
    public RetentionClass RetentionClass { get; set; }
    public DateTime ArchivedUtc { get; set; }
    public string ArchiveReason { get; set; } = string.Empty;
}
