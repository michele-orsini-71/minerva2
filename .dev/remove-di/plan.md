# Remove DI and switch to manual class instantiation

The scope of this project is removing DI code completely and switching to unmanaged/manual instance creation because the current architecture failed its scope.

This doesn't mean abdicating to Clean Architecture principles, we'll still keep our layered architecture and work with interfaces, simply, I do not want DI around anymore. I will refer to Clean Architecture principles and its layered architecture as CALA from now on.

We have to keep in mind that most of the code is already written, but it must be reorganized, so we are going to glue it differently.

**IMPORTANT** The AI Agent is mostly assisting and suggesting, writing code only if and when the user asks.


- we'll start from Minerva Library and then we'll move to Minerva.MarkdownWatcher and finally we'll fix tests
- previously, Minerva had a instance creation phase through extensions and then the code inside startAsync method was executed (which performs migrations and initializes db schema)
- instead, we need to: instantiate the classes, verify options somehow and then start with the operations in startAsync

## Strategy that preserves CALA:

- MinervaBuilder.CreateMinerva(options) async operation
- creates every services Minerva needs and pass options - we should not forget to create the logging service first, with MS Hosting this is granted for free
- each performs options check
- builder calls Task service.PreflightAsync(ct) to verify runtime checks
- after that, the service is ready to start
- every failure is collected with the information that are pertinent to inform the builder calee: what failed and why
- at the end of the chain we have, either a minerva instance to be used or a list of failures to be notified

### About Exceptions of this phase

Skip `AggregateException` — it reads as "something went wrong in parallel work" and people will reach for `InnerExceptions` expecting `Exception` instances, not your `PreflightFailure` records. A bespoke type tells the right story.

## Useful Snippets

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

## Implementation log

### Old code

old code is in .dev/remove-di/old so we have a clean dashboard, we are moving the classes back to their place one by one 

### MinervaOptions

MinervaOptions includes all available options for Minerva

there are also ChatOptions (OpenAICompatibleLlmProvider.cs:207) and SearchOptions (SearchPipeline.cs:50) in the old code, but those are per-call options passed to methods at runtime, not startup configuration.

### Creation sequence

ServiceCollectionExtensions contains all the classes that have to be built, watch out lazy build constructions

logger is injected from the callee because every client will have their preferences

### Creation problems

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

#### Workflows that do not completely fit

Providers: Resolve() of Credentials will be done before creating providers, that's a preflight-like check (environment, like a db connection) during construction but there is not a valid alternative

## Still TODO

- Remove DI from Minerva.Watcher
- Runtime checks we need ensure are present
  - Embedding dimension drift to check 
