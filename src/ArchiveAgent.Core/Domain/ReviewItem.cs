namespace ArchiveAgent.Core.Domain;

/// <summary>A record escalated to a human because confidence was low or a rule blocked auto-action.</summary>
public class ReviewItem
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool Resolved { get; set; }
}
