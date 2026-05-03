# Remove DI and switch to manual class instantiation

The scope of this project is removing DI code completely and switching to unmanaged/manual instance creation because the current architecture failed its scope.

This doesn't mean abdicating to Clean Architecture principles, we'll still keep our layered architecture and work with interfaces, simply, I do not want DI around anymore. I will refer to Clean Architecture principles and its layered architecture as CALA from now on.

We have to keep in mind that most of the code is already written, but it must be reorganized, so we are going to glue it differently.

**IMPORTANT** The AI Agent is mostly assisting and suggesting, writing code only if and when the user asks.


- we'll start from Minerva Library and then we'll move to Minerva.MarkdownWatcher and finally we'll fix tests
- previously, Minerva had a instance creation phase through extensions and then the code inside startAsync method was executed (which performs migrations and initializes db schema)
- instead, we need to: instantiate the classes, verify options somehow and then start with the operations in startAsync

## Phase one: remove DI from MinervaEngine

### Strategy that preserves CALA:

- MinervaBuilder.CreateMinerva(options) async operation
- creates every services Minerva needs and pass options - we should not forget to create the logging service first, with MS Hosting this is granted for free
- each performs options check
- builder calls Task service.PreflightAsync(ct) to verify runtime checks
- after that, the service is ready to start
- every failure is collected with the information that are pertinent to inform the builder calee: what failed and why
- at the end of the chain we have, either a minerva instance to be used or a list of failures to be notified

#### About Exceptions of this phase

Skip `AggregateException` — it reads as "something went wrong in parallel work" and people will reach for `InnerExceptions` expecting `Exception` instances, not your `PreflightFailure` records. A bespoke type tells the right story.

### Useful Snippets

```c#
public sealed class MinervaStartupException : Exception
{
    public IReadOnlyList<PreflightFailure> Failures { get; }
    public MinervaStartupException(IReadOnlyList<PreflightFailure> failures)
        : base(BuildSummary(failures)) { Failures = failures; }
}

// Builder
public static async Task<Minerva> CreateAsync(Options o, CancellationToken ct);
//   on success → returns Minerva
//   on failure → throws MinervaStartupException with the full list

// Inside builder
var failures = new List<PreflightFailure>();
failures.AddIfNotNull(await db.PreflightAsync(ct));
failures.AddIfNotNull(await watcher.PreflightAsync(ct));
// ...
if (failures.Count > 0) throw new MinervaStartupException(failures);
return new Minerva(db, watcher, ...);

// Caller
try { var m = await MinervaBuilder.CreateAsync(opts, ct); /* use it */ }
catch (MinervaStartupException ex)
{
    foreach (var f in ex.Failures) logger.LogError("{Stage}: {Reason}", f.Stage, f.Reason);
}
```

### Implementation log

#### Old code

old code is in .dev/remove-di/old so we have a clean dashboard, we are moving the classes back to their place one by one 

#### MinervaOptions

MinervaOptions includes all available options for Minerva

there are also ChatOptions (OpenAICompatibleLlmProvider.cs:207) and SearchOptions (SearchPipeline.cs:50) in the old code, but those are per-call options passed to methods at runtime, not startup configuration.

#### Creation sequence

ServiceCollectionExtensions contains all the classes that have to be built, watch out lazy build constructions

logger is injected from the callee because every client will have their preferences

#### Creation problems

1. Options shape (e.g. malformed URL, bad connection string format)
  - These are pure-data validation. Discoverable without I/O
  - Make these checks before any contruction with non throwing checks
2. Environmental (e.g. DB unreachable, /embeddings returns 401, pg_vector not installed)
  - Belong in Phase 3 (preflight), on the constructed service

(3. then there will be runtime exceptions, out of scope)

Schema for construction

```c#
// with try/catch only where the API forces it
NpgsqlDataSource? dataSource = null;
try
{
    var b = new NpgsqlDataSourceBuilder(options.ConnectionString);
    b.UseVector();
    dataSource = b.Build();
}
catch (ArgumentException ex)   // Npgsql's shape failures
{
    failures.Add(new PreflightFailure("DataSource", "Invalid connection string.", ex));
}
```

##### Workflows that do not completely fit

Providers: Resolve() of Credentials will be done before creating providers, that's a preflight-like check (environment, like a db connection) during construction but there is not a valid alternative

## Phase two: reshape engine API to bulk-only ingestion

Discovered while planning the client refactor: the per-document `IngestAsync` / `RemoveAsync` on `IMinervaEngine` is a regression from the original Python Minerva, which is bulk-only. Fix this in the library before rebuilding the client — otherwise the client ends up looping a per-doc API that can never legitimately surface deletes.

### Why this is mandatory, not optional

The Python original has one ingestion entry point: `run_incremental_update(collection, new_notes, ...)`. It takes the **complete** source set, fetches existing source IDs and content hashes, set-diffs to compute add/update/delete/unchanged, applies all four, returns aggregate stats.

The new library's `IngestionResult` already has `Added/Updated/Deleted/Unchanged` counters — the data shape was bulk-aware. But the API drifted to per-doc, where `Deleted` can never be populated (a single-doc call can't know what's missing). That's the slip.

### The new shape on `IMinervaEngine`

Remove:

```c#
Task<IngestionResult> IngestAsync(string collectionName, Document document, CancellationToken ct = default);
Task RemoveAsync(string collectionName, string sourceId, CancellationToken ct = default);
```

Replace with a single bulk method:

```c#
Task<IngestionResult> IngestAsync(
    string collectionName,
    IAsyncEnumerable<Document> documents,
    CancellationToken ct = default);
```

`SearchAsync` is unchanged — it's a query, not document manipulation. No single-doc add/remove API will be reintroduced.

### Engine behaviour inside the bulk call

1. **Runtime guardrail.** Compare engine's `(model, dim)` against the collection row. Throw `CollectionEmbedderMismatchException` on mismatch. Cache the engine's own dim at construction so this is two field comparisons per call.
2. **Fetch existing state.** Pull `(sourceId, contentHash)` for every chunk-zero in the collection. Strings only — small.
3. **Stream the input.** For each yielded document:
   - new id → embed + insert (Added)
   - existing id, hash matches → skip (Unchanged)
   - existing id, hash differs → delete old chunks, embed + insert (Updated)
   - mark id as seen
4. **End of stream.** Any existing id not seen → delete (Deleted).

Only the existing-IDs map sits fully in memory. This also opens the door to cross-file embedding batching later without an API change.

### Collection compatibility

Where the check lives: in Minerva. The engine knows the configured embedder; `Collections.GetAsync` already returns stored `embedding_model` and `embedding_dimension`. New surface:

```c#
// On ICollectionService
Task<PreflightFailure?> CheckCompatibilityAsync(string collectionName, CancellationToken ct);
```

Compatibility rule (used both in client preflight and the runtime guardrail above):

| stored                     | configured | result                     |
|----------------------------|------------|----------------------------|
| collection does not exist  |     —      | ok (will create on start)  |
| model and dim both match   |     —      | ok (update path)           |
| anything else              |     —      | mismatch                   |

Why both `model` and `dim`: model name is the primary identity, but Matryoshka-style truncation (OpenAI v3's `dimensions` parameter) lets the same model name produce different output sizes; self-hosted setups can also swap a file behind the same name. Cost is one integer comparison on a row already fetched.

## Phase three: rebuild Minerva.MarkdownWatcher as an indexer

The watcher exists to validate the library. A `FileSystemWatcher` validates `System.IO`, not Minerva. The runtime model becomes a one-shot indexer:

```text
scan tree → bulk ingest → exit
```

The bulk `IngestAsync` from Phase two handles add/update/delete internally, so the indexer needs no per-file event model and no orphan-diff of its own.

**Project rename: `Minerva.MarkdownWatcher` → `Minerva.MarkdownIndexer`.**

### Client behaviour: --force-recreate

Driven by a `--force-recreate` CLI flag (one-shot, never a config key — bypasses the sticky-setting footgun).

- mismatch + flag    → drop + recreate, then full ingestion (collection is empty, everything counts as Added)
- mismatch + no flag → fail with aggregated `MinervaStartupException`
- no collection yet  → create on start
- match              → run normally

### Drop BackgroundService + Host

Replace `MarkdownSyncService : BackgroundService` with a plain class exposing `Task RunAsync(CancellationToken ct)`. Wire by hand the things `Host` was doing.

Deleted entirely (not refactored):

- `FileSystemWatcher` event handlers
- `PathDebouncer`
- `MarkdownIngestionHandler` — was a per-file wrapper around the per-doc API; the bulk API replaces it
- `MarkdownSyncService` — replaced by `MarkdownIndexer`
- `Readiness/` folder, `IRootPathProbe`, `ICollectionDimensionProbe` — checks fold into builder phases (below)
- `DI/ServiceCollectionExtensions.cs`

Surviving:

- `MarkdownScanner` — yields `Document`s lazily for the bulk call
- `WatcherOptions` → renamed `IndexerOptions`

#### Packages

Remove:

- `Microsoft.Extensions.Hosting`

Add:

- `Microsoft.Extensions.Configuration.Json`
- `Microsoft.Extensions.Configuration.EnvironmentVariables`
- `Microsoft.Extensions.Configuration.CommandLine`
- `Microsoft.Extensions.Logging.Console`
- `Microsoft.Extensions.Logging.Configuration`

(`Microsoft.Extensions.Configuration.Binder` already referenced.)

#### Configuration

```c#
var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var minervaOptions = new MinervaOptions();
config.GetSection("Minerva").Bind(minervaOptions);

var indexerOptions = new IndexerOptions();
config.GetSection("Indexer").Bind(indexerOptions);

var forceRecreate = args.Contains("--force-recreate");
```

`launchSettings.json` already sets `DOTNET_ENVIRONMENT`, so debug binding (`appsettings.debug.json`) keeps working.

#### Logging

`LoggerFactory.Create` standalone — this is the `ILoggerFactory` `MinervaBuilder.CreateAsync` already accepts.

```c#
using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConfiguration(config.GetSection("Logging"));
    b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
});
```

The `Logging` section in `appsettings.json` keeps working unchanged.

#### Signal handling

The indexer is a one-shot CLI, not a daemon. `Console.CancelKeyPress` + a `CancellationTokenSource` is enough for Ctrl-C during a long scan. Skip `PosixSignalRegistration` for SIGTERM/SIGQUIT — not load-bearing for a CLI tool.

### Builder

Keep the builder, mirroring `MinervaBuilder`. `MarkdownIndexerBuilder.CreateAsync` runs four phases, throws `MinervaStartupException` on aggregated failure, returns a `MarkdownIndexer` whose `RunAsync` does the work.

Phases:

1. **Validate options** (sync, no I/O): `RootPath` set, `CollectionName` matches regex, `FilePattern` non-empty.
   *(Replaces former `CollectionNameValidCheck`.)*
2. **Construct** (no I/O): scanner.
3. **Preflight** (env): root path exists & readable; `engine.Collections.CheckCompatibilityAsync` (skipped if `--force-recreate`).
   *(Replaces former `RootPathExistsCheck` and `CollectionDimensionMatchCheck`.)*
4. **Start** (side effects): if `--force-recreate` and collection exists, drop + recreate; if collection missing, create. Returns a `MarkdownIndexer` ready to run.

### Final Program.cs shape

1. build configuration
2. build logger factory
3. cts + `Console.CancelKeyPress` handler
4. `engine = await MinervaBuilder.CreateAsync(minervaOptions, loggerFactory, ct)`
5. `indexer = await MarkdownIndexerBuilder.CreateAsync(indexerOptions, engine, loggerFactory, forceRecreate, ct)`
6. `await indexer.RunAsync(ct)`

`indexer.RunAsync` is roughly:

```c#
var documents = scanner
    .ScanFiles()
    .Select(scanner.ReadFile)
    .ToAsyncEnumerable();

var result = await engine.IngestAsync(collectionName, documents, ct);

logger.LogInformation(
    "Sync: +{Added} ~{Updated} -{Deleted} ={Unchanged}",
    result.Added, result.Updated, result.Deleted, result.Unchanged);
```

Exit codes: 0 = success, 2 = preflight failure, 1 = unexpected.

### Update

To match the features of minerva v1, all the collection management went inside MinervaEngine, the indexer simply calls `IngestAsync`

## Future clients (out of scope)

### Live watcher

If a live re-index-on-save tool is later wanted, do not extend any current design — start fresh:

- Single consumer loop fed by a `Channel<FsEvent>`; FSW handlers enqueue, never invoke async work directly.
- Bounded concurrency, per-path serialisation across *all* event types (creates, changes, deletes, the old half of renames).
- Track in-flight tasks for graceful drain on shutdown.
- Backpressure / event coalescing on consumer lag.
- Calls into the engine's bulk `IngestAsync` for batches of accumulated changes — single-doc updates do not exist on the API.

The original `MarkdownSyncService` is a reference for `FileSystemWatcher` wiring only; its concurrency model was wrong.

### Cross-file embedding batching

The bulk `IngestAsync` enables this internally without API change. Once the indexer is real, measure where time goes; if embedder round-trips dominate on many-small-files workloads, buffer chunks across documents up to the embedder's batch size before calling the embedding service.
