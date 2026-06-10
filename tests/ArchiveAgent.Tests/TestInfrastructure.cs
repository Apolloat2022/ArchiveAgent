using ArchiveAgent.Core.Ai;
using ArchiveAgent.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ArchiveAgent.Tests;

/// <summary>
/// A throwaway SQLite in-memory database per test. SQLite (unlike the EF InMemory provider)
/// supports transactions and ExecuteUpdate/ExecuteDelete, so it exercises the real code paths.
/// Keep the connection open for the lifetime of the test or the in-memory DB is dropped.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn;
    public ArchiveDbContext Db { get; }

    public TestDb()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var options = new DbContextOptionsBuilder<ArchiveDbContext>().UseSqlite(_conn).Options;
        Db = new ArchiveDbContext(options);
        Db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}

/// <summary>A fake Claude client that returns a canned JSON response — no network, no cost.</summary>
public sealed class FakeClaudeClient : IClaudeClient
{
    private readonly string _json;
    public FakeClaudeClient(string json) => _json = json;

    public Task<ClaudeResponse> CompleteAsync(string system, string userMessage, CancellationToken ct = default)
        => Task.FromResult(new ClaudeResponse(_json, InputTokens: 10, OutputTokens: 5));
}
