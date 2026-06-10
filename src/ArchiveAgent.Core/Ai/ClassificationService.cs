using System.Text.Json;
using ArchiveAgent.Core.Domain;
using Microsoft.Extensions.Logging;

namespace ArchiveAgent.Core.Ai;

/// <summary>Asks Claude to classify a record, enforces the JSON contract, and re-prompts on bad output.</summary>
public class ClassificationService
{
    private readonly IClaudeClient _claude;
    private readonly ILogger<ClassificationService> _log;

    private const string SystemPrompt = """
        You classify business data records for retention. Output ONLY valid JSON, no prose:
        { "category": "PII|Financial|Operational|Transient",
          "retentionClass": "Permanent|SevenYear|OneYear|Disposable",
          "confidence": 0.0-1.0,
          "reason": "<=20 words" }
        Rules: PII is never Disposable. Financial records are at least SevenYear.
        If unsure, lower the confidence value. Never guess.
        """;

    public ClassificationService(IClaudeClient claude, ILogger<ClassificationService> log)
    {
        _claude = claude;
        _log = log;
    }

    public async Task<(ClassificationResult Result, int Tokens)> ClassifyAsync(Record r, CancellationToken ct = default)
    {
        var user = $$"""
            Record:
            { "externalId": "{{r.ExternalId}}", "type": "{{r.Type}}",
              "createdUtc": "{{r.CreatedUtc:o}}", "content": {{JsonSerializer.Serialize(r.Content)}} }
            """;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var resp = await _claude.CompleteAsync(SystemPrompt, user, ct);
            try
            {
                var result = JsonSerializer.Deserialize<ClassificationResult>(
                    ExtractJson(resp.Text),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result is not null && IsValid(result))
                    return (result, resp.InputTokens + resp.OutputTokens);
            }
            catch (JsonException) { /* fall through to retry */ }

            _log.LogWarning("Unparseable classification for {Id} (attempt {Attempt})", r.ExternalId, attempt);
        }

        // Fallback: low-confidence Unknown so the agent escalates to human review rather than guessing.
        return (new ClassificationResult { Category = "Unknown", RetentionClass = "Unknown", Confidence = 0, Reason = "unparseable model output" }, 0);
    }

    private static bool IsValid(ClassificationResult r) =>
        r.Confidence is >= 0 and <= 1 &&
        new[] { "PII", "Financial", "Operational", "Transient", "Unknown" }.Contains(r.Category);

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }
}
