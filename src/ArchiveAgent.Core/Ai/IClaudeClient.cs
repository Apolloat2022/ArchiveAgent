namespace ArchiveAgent.Core.Ai;

public interface IClaudeClient
{
    Task<ClaudeResponse> CompleteAsync(string system, string userMessage, CancellationToken ct = default);
}

public record ClaudeResponse(string Text, int InputTokens, int OutputTokens);
