namespace ArchiveAgent.Core.Domain;

/// <summary>A live business record awaiting classification/archiving.</summary>
public class Record
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedUtc { get; set; }

    public RecordStatus Status { get; set; } = RecordStatus.Pending;
    public DataCategory Category { get; set; } = DataCategory.Unknown;
    public RetentionClass RetentionClass { get; set; } = RetentionClass.Unknown;
    public double? Confidence { get; set; }
    public string? ClassificationReason { get; set; }
}
