# Sub-PRD: Markdown Watcher Client

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Complete
**Dependency**: [06-sub-prd-api-di.md](./06-sub-prd-api-di.md)
**Last Updated**: 2026-04-18

---

## Implementation Progress

| Step | Description | Status |
| ----- | ------------------------------- | ----------- |
| **1** | Create WatcherOptions | ✅ Complete |
| **2** | Create MarkdownScanner | ✅ Complete |
| **3** | Create MarkdownSyncService | ✅ Complete |
| **4** | Create Program.cs and DI wiring | ✅ Complete |
| **5** | Write tests | ✅ Complete |

---

## Goal

Build the first real consumer of the Minerva library: a generic markdown filesystem watcher (`Minerva.MarkdownWatcher`) that monitors a directory of `.md` files and ingests new/changed/deleted files automatically. Works for Obsidian vaults, repository docs folders, static-site sources (Jekyll/Hugo/Astro/Quartz), or any directory of markdown with optional YAML frontmatter. Obsidian-specific defaults (e.g., excluding `.obsidian/` and `.trash/`) are applied but configurable. This exercises the full ingestion pipeline including incremental updates via content-hash comparison.

Obsidian-specific features (wikilink resolution, embeds, tag extraction, dataview) are explicitly out of scope for this watcher — they would belong in a future `Minerva.Obsidian` extension layered on top.

---

## Implementation Steps

### Step 1: Create WatcherOptions

**File**: `src/Minerva.MarkdownWatcher/WatcherOptions.cs`

```csharp
public class WatcherOptions
{
    public string RootPath { get; set; }            // Root directory to watch
    public string CollectionName { get; set; }      // Minerva collection to ingest into
    public string FilePattern { get; set; } = "*.md";  // Glob for files to watch
    public int DebounceMs { get; set; } = 500;      // Debounce rapid file saves
    public string[] ExcludeDirectories { get; set; } = [".obsidian", ".trash", ".git"];
}
```

`ExcludeDirectories` defaults cover the common cases (Obsidian internals + git metadata). Override the array for other workflows.

### Step 2: Create MarkdownScanner

**File**: `src/Minerva.MarkdownWatcher/MarkdownScanner.cs`

Enumerates and reads markdown files from the root directory:

```csharp
public class MarkdownScanner
{
    public MarkdownScanner(WatcherOptions options) { ... }

    // Enumerate all matching .md files under the root
    public IReadOnlyList<string> ScanFiles() { ... }

    // Read a single file and produce a Document
    public Document ReadFile(string filePath) { ... }
}
```

**ReadFile behavior**:

- Derive `sourceId` from the relative path within the root (e.g., `notes/daily/2026-04-07.md`)
- Parse YAML frontmatter (between `---` fences) into `metadata` dictionary
- Extract `title` from frontmatter `title` field, or fall back to filename without extension
- The remaining markdown content (after frontmatter) becomes the `text`
- Skip files in any `ExcludeDirectories` entry

YAML frontmatter parsing: use a simple parser (split on `---`, parse key-value pairs) or a lightweight library. Don't pull in a heavy YAML dependency for frontmatter.

### Step 3: Create MarkdownSyncService

**File**: `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs`

```csharp
public class MarkdownSyncService : BackgroundService
{
    public MarkdownSyncService(
        MinervaEngine minerva,
        MarkdownScanner scanner,
        WatcherOptions options,
        ILogger<MarkdownSyncService> logger) { ... }

    protected override Task ExecuteAsync(CancellationToken ct) { ... }
}
```

**Behavior**:

1. **Initial sync on startup**: Scan all `.md` files, ingest each via `MinervaEngine.IngestAsync`. The content-hash check inside the ingestion pipeline ensures only new/changed files are actually re-embedded.

2. **Watch for changes**: Set up `FileSystemWatcher` on the root path for `Created`, `Changed`, `Deleted`, and `Renamed` events on `.md` files.

3. **Debouncing**: Editors (Obsidian, VS Code, etc.) save files multiple times in rapid succession. Use a debounce mechanism:
   - On file event, record the file path and timestamp
   - Wait `DebounceMs` before processing
   - If another event arrives for the same file within the window, reset the timer
   - Use a `Channel<string>` or `ConcurrentDictionary<string, CancellationTokenSource>` for debounce tracking

4. **Event handling**:
   - `Created`/`Changed` → `ReadFile` then `IngestAsync`
   - `Deleted` → `RemoveAsync` with the relative path as sourceId
   - `Renamed` → `RemoveAsync` (old path) then `IngestAsync` (new path)

5. **Error handling**: Log and continue on per-file errors. Don't let a single bad file stop the watcher.

### Step 4: Create Program.cs and DI wiring

**File**: `src/Minerva.MarkdownWatcher/Program.cs`

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMinerva(options =>
{
    builder.Configuration.GetSection("Minerva").Bind(options);
});

builder.Services.AddMinervaWatcher(options =>
{
    builder.Configuration.GetSection("Watcher").Bind(options);
});

var host = builder.Build();
await host.RunAsync();
```

**File**: `src/Minerva.MarkdownWatcher/DI/ServiceCollectionExtensions.cs`

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMinervaWatcher(
        this IServiceCollection services,
        Action<WatcherOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<MarkdownScanner>();
        services.AddHostedService<MarkdownSyncService>();
        return services;
    }
}
```

**File**: `src/Minerva.MarkdownWatcher/appsettings.json`

```json
{
  "Minerva": {
    "ConnectionString": "Host=localhost;Database=minerva;Username=minerva",
    "Embedding": {
      "BaseUrl": "http://localhost:11434/v1",
      "Model": "embedding-bge-m3",
      "Concurrency": 1,
      "BatchSize": 1
    }
  },
  "Watcher": {
    "RootPath": "/path/to/markdown/root",
    "CollectionName": "my-notes",
    "DebounceMs": 500
  }
}
```

### Step 5: Write tests

**File**: `tests/Minerva.Tests/Watcher/MarkdownScannerTests.cs`

- Scans a temp directory with `.md` files, returns correct file list
- Excludes directories listed in `ExcludeDirectories`
- Parses YAML frontmatter into metadata dictionary
- Derives sourceId from relative root path
- Falls back to filename as title when frontmatter has no `title`
- Handles files with no frontmatter gracefully

**File**: `tests/Minerva.Tests/Watcher/DebounceTests.cs`

- Rapid file events are coalesced into a single processing call
- Events for different files are processed independently
- Debounce timer resets on each new event for the same file

---

## Files Changed

### New Files

| File | Purpose |
| --------------------------------------------------------------- | ----------------------------------------------------- |
| `src/Minerva.MarkdownWatcher/WatcherOptions.cs` | Watcher configuration |
| `src/Minerva.MarkdownWatcher/MarkdownScanner.cs` | Markdown file enumeration + frontmatter parsing |
| `src/Minerva.MarkdownWatcher/MarkdownSyncService.cs` | BackgroundService with FileSystemWatcher + debouncing |
| `src/Minerva.MarkdownWatcher/Program.cs` | Host builder entry point |
| `src/Minerva.MarkdownWatcher/DI/ServiceCollectionExtensions.cs` | AddMinervaWatcher() extension |
| `src/Minerva.MarkdownWatcher/appsettings.json` | Default configuration |
| `tests/Minerva.Tests/Watcher/MarkdownScannerTests.cs` | Scanner unit tests |
| `tests/Minerva.Tests/Watcher/DebounceTests.cs` | Debounce logic unit tests |

---

## Verification Checklist

- [ ] `dotnet build src/Minerva.MarkdownWatcher` compiles without errors
- [ ] MarkdownScanner correctly parses frontmatter and derives sourceId
- [ ] MarkdownScanner excludes directories listed in `ExcludeDirectories`
- [ ] Debounce logic coalesces rapid events for the same file
- [ ] `dotnet run --project src/Minerva.MarkdownWatcher` starts without error (with valid config)
- [ ] Dropping a `.md` file into a test root triggers ingestion (manual verification)
- [ ] Run: `dotnet test tests/Minerva.Tests --filter Category=Watcher`

⏸️ **GATE**: Sub-PRD complete. `/dev-checkpoint`.
