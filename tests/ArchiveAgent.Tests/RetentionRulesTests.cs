using ArchiveAgent.Core.Agents;
using ArchiveAgent.Core.Domain;
using Xunit;

namespace ArchiveAgent.Tests;

/// <summary>Pure unit tests for the deterministic guardrails — fast, no DB, no network.</summary>
public class RetentionRulesTests
{
    private static Record Old() => new() { CreatedUtc = DateTime.UtcNow.AddDays(-800) };

    [Fact]
    public void Decide_LowConfidence_ReturnsReview()
    {
        var action = RetentionRules.Decide(new Record(), DataCategory.Operational, RetentionClass.Disposable, confidence: 0.5);
        Assert.Equal(AgentAction.Review, action);
    }

    [Fact]
    public void Decide_UnknownCategory_ReturnsReview()
    {
        var action = RetentionRules.Decide(new Record(), DataCategory.Unknown, RetentionClass.Disposable, confidence: 0.99);
        Assert.Equal(AgentAction.Review, action);
    }

    [Theory]
    [InlineData(RetentionClass.Disposable)]
    [InlineData(RetentionClass.OneYear)]
    public void Decide_ShortLivedAndConfident_ReturnsArchive(RetentionClass retention)
    {
        var action = RetentionRules.Decide(new Record(), DataCategory.Operational, retention, confidence: 0.9);
        Assert.Equal(AgentAction.Archive, action);
    }

    [Fact]
    public void Decide_Permanent_ReturnsKeep()
    {
        var action = RetentionRules.Decide(new Record(), DataCategory.Financial, RetentionClass.Permanent, confidence: 0.95);
        Assert.Equal(AgentAction.Keep, action);
    }

    [Fact]
    public void VerifyArchive_Pii_IsBlocked()
    {
        var ok = RetentionRules.VerifyArchive(Old(), DataCategory.PII, out var reason);
        Assert.False(ok);
        Assert.Contains("PII", reason);
    }

    [Fact]
    public void VerifyArchive_UnderMinimumAge_IsBlocked()
    {
        var recent = new Record { CreatedUtc = DateTime.UtcNow.AddDays(-10) };
        var ok = RetentionRules.VerifyArchive(recent, DataCategory.Operational, out _);
        Assert.False(ok);
    }

    [Fact]
    public void VerifyArchive_OldNonPii_Passes()
    {
        var ok = RetentionRules.VerifyArchive(Old(), DataCategory.Operational, out _);
        Assert.True(ok);
    }
}
