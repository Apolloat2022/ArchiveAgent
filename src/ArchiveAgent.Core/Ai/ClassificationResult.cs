namespace ArchiveAgent.Core.Ai;

/// <summary>The structured JSON shape we require Claude to return.</summary>
public class ClassificationResult
{
    public string Category { get; set; } = "Unknown";        // PII | Financial | Operational | Transient
    public string RetentionClass { get; set; } = "Unknown";  // Permanent | SevenYear | OneYear | Disposable
    public double Confidence { get; set; }                    // 0.0 - 1.0
    public string Reason { get; set; } = string.Empty;
}
