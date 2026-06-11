# Running and debugging Minerva (the watcher client)

Operational reference for the `Minerva.MarkdownWatcher` client: how it boots,
how the "event loop" actually works, the preflight probes, and the PostgreSQL
mechanics behind migrations. Distilled from the 2024-04 full-run notes.

## Configuration

`Host.CreateApplicationBuilder` auto-loads `appsettings.{Environment}.json`
on top of `appsettings.json` when `DOTNET_ENVIRONMENT` is set — no code
change needed.

```
appsettings.json              # shared defaults
appsettings.WorkVault.json     # { "Watcher": { "RootPath": "/work", "CollectionName": "work" } }
appsettings.PersonalVault.json
```

```bash
DOTNET_ENVIRONMENT=WorkVault dotnet run --project src/Minerva.MarkdownWatcher
# or override individual keys via CLI args:
dotnet run --project src/Minerva.MarkdownWatcher -- \
  --Watcher:RootPath=/Users/michele/vault-a \
  --Watcher:CollectionName=vault-a \
  --Minerva:Llm:Model=gemma-3-4b
```

Log levels (`appsettings.debug.json`):

```json
"Logging": { "LogLevel": {
  "Default": "Information", "Minerva": "Debug",
  "Minerva.MarkdownWatcher": "Debug", "Microsoft": "Warning"
}}
```

## Checking dependencies are up

```bash
brew services list                                   # postgres

curl http://127.0.0.1:1234/v1/embeddings \           # LM Studio embedder
  -H "Content-Type: application/json" \
  -d '{"model":"text-embedding-bge-m3","input":"Some text to embed"}'

curl http://localhost:1234/v1/chat/completions \     # LM Studio LLM
  -H "Content-Type: application/json" \
  -d '{"model":"google/gemma-3-4b","messages":[{"role":"user","content":"hi"}],"max_tokens":100}'
```

When ingesting and contextualizing at once, load all needed models into the
local server in parallel; otherwise it swaps models per chunk. For
contextualization the working model is `gemma-3-4b` (vision/instruct/large
variants are not used here).

## How the app runs — Generic Host, not a one-shot

`Minerva.MarkdownWatcher` is a long-lived **Generic Host** process (same base
as an ASP.NET Core app or a Windows Service). `RunAsync` owns the lifetime:

- **Start.** `IHostedService`s start in registration order.
  `MinervaStartupService` awaits schema initialization (migrations run to
  completion) before the next service. `MarkdownSyncService` (a
  `BackgroundService`) has its `ExecuteAsync` fired as a detached task;
  startup is "done" once it hits its first `await`.
- **Running.** `RunAsync` awaits a task that only completes on shutdown. The
  process is alive because of two things: `ExecuteAsync` parked at
  `await Task.Delay(Timeout.Infinite, stoppingToken)`, and a
  `FileSystemWatcher` callback registered with the OS. There is **no
  `while(true)`** — file changes are pushed to the thread pool by the OS, the
  debouncer fires via timer continuations, and shutdown is a cancellation
  token.
- **Stop.** SIGINT / Ctrl-C cancels `stoppingToken` →
  `Task.Delay(Infinite, …)` throws `OperationCanceledException` →
  `ExecuteAsync` returns → hosted services stop in reverse order → the watcher
  and debouncer are disposed → `RunAsync` returns → process exits.

### Debugging from here

Once `RunAsync` is on the stack, "step over" stops being useful — you wait
for external events. Let startup complete (F5) and watch for: "Applying
migration …" (Postgres reachable), "Creating collection … with model …"
(embedder reachable), "Initial scan: N files" (vault path correct). Useful
breakpoints: `MarkdownIngestionHandler.OnChangedAsync` (once per file on
initial scan) and the cold path in `IngestionPipeline`. For live events,
breakpoint the `Created`/`Changed` lambdas in `MarkdownSyncService` and touch
a `.md` file. Do **not** breakpoint the `Task.Delay(Infinite, …)` line — it
hits instantly and just sits.

**Debug footgun:** some IDEs (Rider) pause the process, not the wall clock, so
on resume the debouncer's timers may fire immediately and collapse several
debounce windows into one. Verify coalescing with a timed test (two saves
within `DebounceMs`), not from a breakpoint session.

## Preflight checks — the probe facades

Startup readiness probes (embedding dimension check, LLM availability) call
the model server through tiny `internal` facade interfaces
(`IEmbeddingProbeFacade`, `IChatClientFacade`), each with one production
implementation delegating to the OpenAI SDK. The **hot paths**
(`OpenAICompatibleEmbeddingProvider`, `OpenAICompatibleLlmProvider`) call the
SDK directly, wrapped in Polly + a rate limiter; the facades are used **only**
on probe paths, which must **bypass** Polly and the rate limiter.

The seam exists because the OpenAI SDK's `EmbeddingClient` / `ChatClient` are
sealed with no test double. The facade (≈15 lines per provider, one extra
virtual call) lets unit tests assert probe-specific invariants: HTTP 400 →
LLM check still passes (reasoning-model exception); call count = 1 (proves
Polly bypass — Polly would retry 3×); failure caching and cancellation
discipline for the embedding probe. This is a seam for a sealed external
boundary, not a test framework leaking into production.

## PostgreSQL mechanics

### Creating the initial database

Connect as a superuser (your OS user, e.g. `michele`, via local-socket trust
auth — no password), then:

```sql
CREATE ROLE minerva WITH LOGIN PASSWORD 'minerva';   -- run from the postgres DB
CREATE DATABASE minerva OWNER minerva;
-- then, connected to the minerva DB:
CREATE EXTENSION IF NOT EXISTS vector;
SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';
```

### `pg_advisory_lock` — the migration mutex

Migrations are guarded by a PostgreSQL advisory lock — a **distributed mutex**:

```csharp
await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@id)", conn))
```

`pg_advisory_lock` is a built-in function; you invoke a function by
`SELECT`ing it (it returns void, so the code uses `ExecuteNonQueryAsync`).
Postgres keeps an in-memory per-session lock table (`pg_locks`): the first
caller for an id acquires it and returns immediately; later callers for the
same id **block** until release. It is:

- **Session-bound** — if the app crashes mid-migration, the connection drops
  and the lock auto-releases (no stuck semaphore); the `finally` unlock is
  belt-and-braces.
- **Cooperative** — it does not stop writes without the lock; it only blocks
  other callers asking for the *same* id.

Inspect held advisory locks:

```sql
SELECT l.pid, l.mode, l.granted,
       (l.classid::bigint << 32) | l.objid::bigint AS lock_id,
       a.application_name, a.state, a.query
FROM pg_locks l LEFT JOIN pg_stat_activity a ON a.pid = l.pid
WHERE l.locktype = 'advisory';
```

Minerva's lock id encodes ASCII: `classid` `0x004D494E` = "MIN", `objid`
`0x45525601` = "ERV"+1, combining to `0x004D494E45525601` = "MINERV" + "01".

### Migrations

Applied migrations are recorded in a table; pending ones live as embedded SQL
resources under `Minerva.Storage.Migrations.*`, sorted by resource name with
`StringComparer.Ordinal` — hence the zero-padded prefixes (`001_initial.sql`,
`002_indexes.sql`, …) for a deterministic, locale-independent apply order.
Each migration runs in a transaction that both applies the DDL and records it
in the migration table, so the two stay consistent. Most PostgreSQL DDL is
transactional (unlike MySQL), which is what makes this pattern clean. Keep
migrations cheap and schema-only — heavy data backfills do not belong in
startup migrations (see the FTS-fix note for why a `to_tsvector` rebuild as a
migration broke startup).
