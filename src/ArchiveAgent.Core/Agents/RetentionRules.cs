using ArchiveAgent.Core.Domain;

namespace ArchiveAgent.Core.Agents;

/// <summary>
/// Deterministic guardrails. The LLM proposes; these rules dispose.
/// Nothing irreversible runs without passing VerifyArchive.
/// </summary>
public static class RetentionRules
{
    public const double ConfidenceFloor = 0.75;
    public static readonly TimeSpan MinAgeBeforeArchive = TimeSpan.FromDays(365);

    /// <summary>DECIDE: combine the model's classification with rules into an action.</summary>
    public static AgentAction Decide(Record r, DataCategory category, RetentionClass retention, double confidence)
    {
        if (confidence < ConfidenceFloor || category == DataCategory.Unknown)
            return AgentAction.Review;

        // Short-lived classes are candidates to move out of the hot table.
        if (retention is RetentionClass.Disposable or RetentionClass.OneYear)
            return AgentAction.Archive;

        return AgentAction.Keep;
    }

    /// <summary>VERIFY: the gate that MUST pass before an archive action commits.</summary>
    public static bool VerifyArchive(Record r, DataCategory category, out string reason)
    {
        if (category == DataCategory.PII)
        {
            reason = "blocked: PII may not be auto-archived";
            return false;
        }
        if (DateTime.UtcNow - r.CreatedUtc < MinAgeBeforeArchive)
        {
            reason = "blocked: below minimum retention age";
            return false;
        }
        reason = "passed verification";
        return true;
    }
}
