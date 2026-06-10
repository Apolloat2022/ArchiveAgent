# ArchiveAgent

[![CI](https://github.com/Apolloat2022/ArchiveAgent/actions/workflows/ci.yml/badge.svg)](https://github.com/Apolloat2022/ArchiveAgent/actions/workflows/ci.yml)

**Agentic data archiving & classification for .NET — powered by Claude.**

ArchiveAgent uses an LLM (Anthropic's Claude) to classify business records and propose a retention
action, while **deterministic C# rules verify and execute** the action against SQL Server via Entity
Framework Core. The model proposes; the rules dispose. A wrong AI answer can never cause a wrong
irreversible action.

> See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design, the safety model, and rationale.

---

## What it does

Most systems archive data with a blunt rule — *"move everything older than N days"* — which can't tell a
disposable log from a customer's personal information. ArchiveAgent replaces that with an **agent loop**.
For each record it:

1. **Classifies** it with Claude → a data **category** (PII / Financial / Operational / Transient) and a
   **retention class**, returned as strict JSON.
2. **Decides** an action with deterministic rules — Keep, Archive, or Review.
3. **Verifies** before acting — a hard gate that vetoes anything unsafe (PII is never auto-archived,
   nothing under a minimum age is archived), regardless of what the model said.
4. **Acts** — moves the record to the archive via a transactional EF Core operation.
5. **Logs** every decision (reason, confidence, token cost) and escalates anything low-confidence to a
   human review queue.

```
ingest → classify (Claude) → decide (rules) → verify (gate) → act (archive) → log → escalate
```

## Features

- **Agentic loop** with an independent, deterministic **verification gate** — safety is enforced in
  tested code, not entrusted to the LLM.
- **Structured, validated LLM output** (JSON contract, re-prompt on failure, low-confidence fallback).
- **Entity Framework Core** data layer with a **set-based stored-procedure → EF migration**
  (`RecordArchiver`, using `ExecuteUpdate`).
- **Full audit trail** and a **human review queue** for escalations.
- **ASP.NET Core Web API** to run and monitor the pipeline, with Swagger.
- **xUnit test suite** that runs **offline** (SQLite in-memory + a fake LLM client — no API key, no cost).
- **GitHub Actions CI** (build + test on every push/PR).

## Tech stack

C# 12 · .NET 8 · ASP.NET Core (minimal APIs) · Entity Framework Core 8 · SQL Server (SQLite for tests) ·
Anthropic Claude (typed `HttpClient` + Polly) · xUnit · GitHub Actions

## Project structure

```
ArchiveAgent.sln
├── src/
│   ├── ArchiveAgent.Api/        ASP.NET Core Web API — run + monitor the pipeline
│   └── ArchiveAgent.Core/
│       ├── Domain/              Record, ArchivedRecord, AuditLog, ReviewItem, enums
│       ├── Data/                ArchiveDbContext, DbSeeder, RecordArchiver (proc → EF)
│       ├── Ai/                  ClaudeClient (HttpClient + Polly) + ClassificationService
│       ├── Agents/              RetentionRules (guardrails) + ArchiveAgentService (the loop)
│       └── Legacy/              sp_ArchiveOldRecords.sql + MIGRATION.md
├── tests/ArchiveAgent.Tests/    xUnit (SQLite in-memory + fake Claude client)
└── docs/ARCHITECTURE.md
```

## Getting started

**Prerequisites:** .NET 8 SDK (or Visual Studio 2022), SQL Server LocalDB, and an Anthropic API key.

```bash
dotnet restore

# store the API key outside source control (user-secrets):
cd src/ArchiveAgent.Api
dotnet user-secrets init
dotnet user-secrets set "Claude:ApiKey" "sk-ant-..."

dotnet run
```

The database is auto-created and seeded with sample data on first run. Open the Swagger UI, then:

| Endpoint | Purpose |
|---|---|
| `POST /pipeline/run` | Run one batch through the agent loop |
| `GET /records?status=Pending` | List records by status |
| `GET /archive` | View archived records |
| `GET /audit` | Every decision + token usage |
| `GET /review-queue` | Low-confidence / rule-blocked escalations |

## Tests

```bash
dotnet test
```

The suite runs **offline** — no API key, no network — using SQLite in-memory (real transactions and
`ExecuteUpdate`) and a fake Claude client:

- **`RetentionRulesTests`** — the deterministic guardrails (PII blocked, under-age blocked, confidence floor).
- **`ArchiveAgentServiceTests`** — the full agent loop, including the verification gate blocking PII,
  low-confidence, and under-age records.
- **`RecordArchiverTests`** — the stored-procedure → EF parity test (archives old non-PII rows, leaves PII
  and recent rows untouched, idempotent on re-run).

## Roadmap

- Split the classifier and archiver into independent services communicating over a queue (microservices).
- Add embeddings + a vector store for semantic search ("RAG") over the archive.
- EF Core migrations (the demo uses `EnsureCreated()` + seeding for convenience).
- Bounded-parallel batch classification and cost/eval dashboards.
- An Azure DevOps pipeline alongside the GitHub Actions workflow.

## Notes

- Default model is `claude-sonnet-4-6`; a cheaper model (e.g. `claude-haiku-4-5`) works well for
  classification — set it in `appsettings.json` → `Claude:Model`.
- The API key is read from user-secrets and is never committed (`.gitignore` excludes secrets).

## License

MIT — see [LICENSE](LICENSE).
