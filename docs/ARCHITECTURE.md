# ArchiveAgent — Architecture

> A .NET 8 reference implementation of an **agentic data-archiving and classification system**.
> Claude (Anthropic) classifies business records and proposes a retention action; **deterministic C#
> rules verify and execute** the action against SQL Server via Entity Framework Core. The LLM proposes;
> the rules dispose.

---

## 1. What it does

### In plain terms

Think of it as a **smart, careful filing clerk for a company's data.**

Companies store millions of old records — invoices, logs, customer notes. Eventually a lot of it has to be
moved into long-term storage ("archived") to keep the live system fast. Most companies do this with a crude
rule: *"anything older than X, box it up."* That rule is blind — it can't tell junk from sensitive personal
information, so it either archives too much (and breaks compliance rules) or someone does it by hand (slow
and error-prone).

ArchiveAgent makes that decision intelligent **and** safe. For each record it:

1. **Reads it** and works out what kind of data it is (personal info? a financial record? a throwaway log?).
2. **Decides** what to do — leave it, archive it, or flag it for a human.
3. **Double-checks before acting**, against strict safety rules it cannot break (e.g. *personal info is
   never auto-archived*; *nothing too recent is touched*).
4. **Files it away** properly if it's safe to.
5. **Keeps a receipt** of every decision, and hands anything it's unsure about to a person.

The most important part: it uses AI to make the smart call, but **the AI is never allowed to do anything
risky on its own** — a hard rulebook sits on top and can overrule it. The AI *suggests*; the rules
*decide*. So even if the AI is wrong, the system won't do anything it shouldn't.

> In short: it turns a slow, risky, manual data-cleanup job into something automatic, consistent, and
> safe — with a built-in safety net so sensitive information is never mishandled.

### In technical terms

Legacy systems archive data with blunt rules — usually a stored procedure that says "move everything
older than N days." ArchiveAgent replaces that with an **agent loop**: for each record it (1) asks Claude
to classify the record into a data **category** (PII / Financial / Operational / Transient) and a
**retention class**, (2) applies deterministic business rules to **decide** an action (Keep / Archive /
Review), (3) passes the action through a **verification gate** that can veto it (e.g., never auto-archive
PII, never archive below a minimum age), (4) **executes** the archive as a transactional EF Core move, and
(5) writes an **audit record** of every decision. Anything the model is unsure about, or that a rule
blocks, is escalated to a human **review queue**. It is designed so that a wrong LLM answer can never
cause a wrong irreversible action.

---

## 2. Solution structure

```
ArchiveAgent.sln
├── src/
│   ├── ArchiveAgent.Api    (ASP.NET Core minimal-API host — trigger & monitor the pipeline)
│   └── ArchiveAgent.Core   (class library — all domain, data, AI, and agent logic)
└── tests/
    └── ArchiveAgent.Tests  (xUnit — SQLite in-memory + a fake Claude client; runs offline)
```

**Dependency direction:** `Api → Core`, `Tests → Core`. The Core library has no dependency on ASP.NET;
it is host-agnostic and could run from a worker service, a console app, or an Azure Function unchanged.

| Project | Key packages |
|---|---|
| Core | `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Extensions.Http`, `Polly`, `Microsoft.Extensions.{Options,Logging.Abstractions}` |
| Api | `Microsoft.EntityFrameworkCore.SqlServer`, `Swashbuckle.AspNetCore` |
| Tests | `xunit`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.NET.Test.Sdk` |

---

## 3. Domain model

Four entities (in `Core/Domain`), persisted via EF Core. Enums are stored as **strings** (via
`HasConversion<string>`) so the database is human-readable and resilient to enum reordering.

| Entity | Purpose |
|---|---|
| `Record` | A live business record awaiting processing. Carries classification results once processed. |
| `ArchivedRecord` | A record moved out of the hot table into the archive (with `ArchivedUtc`, `ArchiveReason`). |
| `AuditLog` | Immutable log of every agent decision (action, reason, confidence, tokens). |
| `ReviewItem` | A record escalated to a human (low confidence or rule-blocked). |

**Record lifecycle (state machine):**

```
Pending ──classify──▶ Classified ──(Keep)
   │
   ├──(Archive + verify pass)──▶ Archived
   │
   └──(low conf / rule veto)───▶ NeedsReview ──▶ ReviewItem (human)
```

Enums: `RecordStatus`, `DataCategory`, `RetentionClass`, `AgentAction`.

---

## 4. The agent pipeline (data flow)

`ArchiveAgentService.RunAsync(batchSize)` is the heart of the system:

```
                ┌─────────────────────────────────────────────────────────────┐
                │                    ArchiveAgentService                        │
                │                                                               │
  Records       │  1. INGEST     query Status == Pending (batch)               │
  (Pending) ───▶│  2. CLASSIFY   ClassificationService ──▶ Claude (JSON)       │──▶ Claude API
                │  3. DECIDE     RetentionRules.Decide(rules + confidence)      │
                │  4. VERIFY     RetentionRules.VerifyArchive (the GATE) ✋     │
                │  5a. ACT       archive (transactional EF move)  ─────────────┼──▶ ArchivedRecord
                │  5b. ESCALATE  low-conf / vetoed ─▶ ReviewItem               │──▶ ReviewItem
                │  6. LOG        AuditLog (reason, confidence, tokens)         │──▶ AuditLog
                └─────────────────────────────────────────────────────────────┘
```

**Step detail:**
1. **Ingest** — `Where(r => r.Status == Pending).OrderBy(Id).Take(batchSize)`.
2. **Classify** — `ClassificationService` sends the record to Claude with a strict JSON contract and
   parses `{ category, retentionClass, confidence, reason }`. Bad JSON → one re-prompt → then a
   low-confidence `Unknown` fallback (which forces review rather than a guess).
3. **Decide** — `RetentionRules.Decide`: below the confidence floor (0.75) or `Unknown` → **Review**;
   short-lived classes (`Disposable`/`OneYear`) → **Archive**; otherwise → **Keep**.
4. **Verify** — `RetentionRules.VerifyArchive`: **PII is never auto-archived**; nothing under the minimum
   retention age (365 days) is archived. This gate is deterministic and independent of the model.
5. **Act / Escalate** — a verified archive moves the row to `ArchivedRecord` and flips status to
   `Archived` **inside a transaction**; a vetoed or low-confidence record becomes a `ReviewItem`.
6. **Log** — every outcome writes an `AuditLog` with the reason, confidence, and token usage.

Returns a `RunSummary { Processed, Archived, Kept, Review, TokensUsed }`.

---

## 5. Key components (what a .NET reviewer will look at)

### `ArchiveDbContext` (EF Core)
- `DbSet`s for the four entities; `OnModelCreating` configures indexes (`Status`, `CreatedUtc`,
  `OriginalRecordId`, `Resolved`), max lengths, and **enum→string** conversions.
- Provider-agnostic: SQL Server in the app, SQLite in tests — same model, same code paths.

### `ClaudeClient` (`IClaudeClient`)
- A **typed `HttpClient`** (registered via `AddHttpClient<IClaudeClient, ClaudeClient>()`), so connection
  pooling and lifetime are managed by `IHttpClientFactory`.
- Calls the Anthropic **Messages API** with `x-api-key` + `anthropic-version` headers, `temperature = 0`.
- **Polly** retry policy: exponential backoff on HTTP 429 and 5xx.
- Returns `ClaudeResponse(Text, InputTokens, OutputTokens)` for cost tracking.

### `ClassificationService`
- Owns the **prompt contract**: a system prompt that pins the output to JSON and encodes hard rules
  ("PII is never Disposable", "Financial ≥ SevenYear").
- **Defensive parsing**: extracts the JSON span, deserializes, validates ranges/enum values, re-prompts
  once on failure, and falls back to a low-confidence `Unknown` so failures escalate, not slip through.

### `RetentionRules` (the guardrails)
- Pure, static, **side-effect-free** functions → trivially unit-testable.
- `Decide(...)` turns (category, retention, confidence) into an `AgentAction`.
- `VerifyArchive(...)` is the **safety gate** that must pass before any archive commits.

### `ArchiveAgentService` (the loop)
- Orchestrates the pipeline; archives via a **`BeginTransactionAsync` … Commit`** so the insert +
  status flip are atomic.
- Fully `async`/`CancellationToken`-threaded.

### `RecordArchiver` (the stored-proc → EF migration)
- A faithful, **set-based** EF Core reimplementation of the legacy `sp_ArchiveOldRecords`:
  - projects matching rows and bulk-inserts into `ArchivedRecord`,
  - flips status with **`ExecuteUpdateAsync`** (EF Core 7+) — no row-by-row loop, no entities tracked,
  - wrapped in a transaction.
- This is the concrete artifact for the "convert stored procedures into Entity Framework" requirement.

---

## 6. Reliability & safety model (the important part)

LLMs are non-deterministic; production data operations cannot be. ArchiveAgent isolates the uncertainty:

| Risk | Mitigation |
|---|---|
| Model returns malformed output | Strict JSON contract + validation + re-prompt + `Unknown` fallback → review |
| Model is wrong but confident | **Deterministic verification gate** vetoes unsafe actions regardless of the model |
| Sensitive data mishandled | Hard rule: PII never auto-archived; min-age floor |
| Model low confidence | Confidence floor (0.75) routes to human review |
| Action not auditable | Every decision writes an `AuditLog` (reason, confidence, tokens) |
| Partial failure mid-archive | Per-record transaction; archive is a reversible *move*, not a delete |

The mental model: **Claude is an advisor inside a deterministic state machine, never the actor of record.**

---

## 7. Testing strategy

`ArchiveAgent.Tests` (xUnit) runs **offline — no API key, no cost**:
- **SQLite in-memory** (not the EF InMemory provider) so transactions and `ExecuteUpdate` behave like a
  real relational DB.
- A **`FakeClaudeClient`** returns canned JSON, making the agent deterministic under test.
- Coverage: the guardrails (`RetentionRulesTests`), the full loop incl. the veto paths
  (`ArchiveAgentServiceTests`), and the **proc↔EF parity + idempotency** (`RecordArchiverTests`).

CI (GitHub Actions) runs `restore → build → test` on every push/PR.

---

## 8. Tech stack

| Layer | Technology |
|---|---|
| Language / runtime | C# 12, .NET 8 |
| Web | ASP.NET Core minimal APIs, Swagger (Swashbuckle) |
| Data | Entity Framework Core 8, SQL Server (SQLite for tests) |
| AI | Claude (Anthropic Messages API) via typed `HttpClient` |
| Resilience | Polly (retry/backoff) |
| Tests | xUnit |
| CI | GitHub Actions |

---

## 9. Design decisions & rationale

- **EF Core over raw ADO/SP** — testability, type-safety, and a clear path to migrate stored-proc
  logic into managed code; performance-critical set operations still use `ExecuteUpdate`/`ExecuteDelete`.
- **Rules separate from the agent** — the safety logic is pure and unit-tested in isolation; the agent
  just orchestrates.
- **Typed HttpClient + Polly** — correct client lifetime and resilient external calls without bespoke
  plumbing.
- **Strings for enums in the DB** — readable data, safe against enum reordering.
- **SQLite for tests** — relational fidelity (transactions, `ExecuteUpdate`) without a server dependency.

---

## 10. Known limitations (it's a scaffold — deliberately)

A .NET reviewer should know what's intentionally simplified:
- **Schema:** uses `Database.EnsureCreated()` + seeding for convenience. Production should use **EF
  migrations**.
- **Throughput:** classification is sequential per record in `RunAsync`. Real scale would add **bounded
  parallelism** (`Parallel.ForEachAsync` + `SemaphoreSlim`) sized to the Anthropic rate limit, and batch
  the archive moves.
- **`ExecuteUpdate` + tracking:** it bypasses the change tracker, so callers must re-read (or
  `ChangeTracker.Clear()`) to observe updated state — documented in `RecordArchiverTests`.
- **API:** no authN/Z, no rate limiting — it's a demo host, not a public surface.
- **Cost/eval dashboards:** tokens are logged but not yet aggregated/alerted.

---

## 11. Roadmap (natural extensions)

1. **Microservices** — split `Classifier` and `Archiver` into separate services communicating over a
   queue (e.g., Azure Service Bus) with idempotent consumers; the bounded contexts are already clean.
2. **RAG over the archive** — embed archived content into a vector store (e.g., Azure AI Search) so the
   archive is semantically searchable, and retrieve similar records to ground classification.
3. **Azure DevOps** — pipeline YAML, EF migrations applied through gated environments, Claude Code in a
   PR-reviewed migration workflow.
4. **Background scheduling** — an `IHostedService` to run batches on a cadence.
```
