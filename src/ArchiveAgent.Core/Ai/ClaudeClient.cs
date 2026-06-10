using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace ArchiveAgent.Core.Ai;

public class ClaudeOptions
{
    public string ApiKey { get; set; } = string.Empty;          // set via user-secrets: "Claude:ApiKey"
    public string Model { get; set; } = "claude-sonnet-4-6";    // claude-haiku-4-5 is cheaper for classification
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public int MaxTokens { get; set; } = 512;
}

/// <summary>Thin async client over the Anthropic Messages API with retry/backoff.</summary>
public class ClaudeClient : IClaudeClient
{
    private readonly HttpClient _http;
    private readonly ClaudeOptions _opt;
    private readonly ILogger<ClaudeClient> _log;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retry;

    public ClaudeClient(HttpClient http, IOptions<ClaudeOptions> opt, ILogger<ClaudeClient> log)
    {
        _http = http;
        _opt = opt.Value;
        _log = log;
        _retry = Policy
            .HandleResult<HttpResponseMessage>(r => (int)r.StatusCode == 429 || (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }

    public async Task<ClaudeResponse> CompleteAsync(string system, string userMessage, CancellationToken ct = default)
    {
        var body = new
        {
            model = _opt.Model,
            max_tokens = _opt.MaxTokens,
            temperature = 0.0,
            system,
            messages = new[] { new { role = "user", content = userMessage } }
        };

        var resp = await _retry.ExecuteAsync(async () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _opt.BaseUrl) { Content = JsonContent.Create(body) };
            req.Headers.Add("x-api-key", _opt.ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            return await _http.SendAsync(req, ct);
        });

        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var text = root.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
        var usage = root.GetProperty("usage");
        return new ClaudeResponse(text, usage.GetProperty("input_tokens").GetInt32(), usage.GetProperty("output_tokens").GetInt32());
    }
}
