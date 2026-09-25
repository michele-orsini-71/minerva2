# Minerva.MarkdownIndexer

A filesystem watcher that keeps a Minerva collection in sync with a directory of
markdown files. The first real client of the [Minerva](../Minerva/README.md)
library.

Works for:

- Obsidian vaults
- Repository `docs/` folders
- Static-site sources (Jekyll, Hugo, Astro, Quartz, …)
- Any directory of `.md` files with optional YAML frontmatter

Obsidian-specific features (wikilinks, embeds, tags, dataview) are **out of
scope** — those would belong in a future `Minerva.Obsidian` extension layered on
top.

## What it does

On startup:

1. Ensures the target Minerva collection exists (auto-creates it, probing the
   embedder for its vector dimension).
2. Scans the root directory and ingests every matching file.
3. Starts a `FileSystemWatcher` with debouncing; thereafter each
   create/change/delete/rename is reflected in the collection.

Re-ingestion is cheap because Minerva dedupes by chunk content hash — unchanged
chunks are skipped.

Frontmatter is parsed and passed through as document `Metadata`. Markdown images
(`![alt](path)`) are extracted into `AttachmentDescription`s using the alt text
as the description.

## Usage

```bash
dotnet run --project src/Minerva.MarkdownIndexer
```

Runs as a long-lived host — it does not exit until cancelled.

## Configuration

`appsettings.json`:

```json
{
  "Minerva": {
    "ConnectionString": "Host=localhost;Database=minerva;Username=minerva;Password=minerva",
    "Embedding": {
      "BaseUrl": "http://localhost:11434/v1",
      "Model": "embedding-bge-m3"
    }
  },
  "Indexer": {
    "RootPath": "/path/to/markdown/root",
    "CollectionName": "my-notes",
    "Description": "My personal notes: projects, how-tos, reading notes",
    "FileExtensions": ["md"],
    "ExcludeDirectories": [".obsidian", ".trash", ".git"],
    "AllowRecreateOnConfigMismatch": false,
    "AllowSourceScopeChange": false
  }
}
```

The `Minerva` section is the full core-library config (see
[`src/Minerva/README.md`](../Minerva/README.md)); the `Indexer` section
configures this client (bound to [`IndexerOptions`](IndexerOptions.cs)).

Every `Indexer` key except `Description` is required; the binder reports a
clear error if any is missing. `Description` tells MCP clients what the
collection contains, so they can pick it without being told its name; it is
stored only when the collection is created. `FileExtensions` lists the file types to index as bare extensions such
as `md` or `txt` (matching is case- and dot-insensitive, so `md`, `.md` and
`.MD` are equivalent; a glob like `*.md` is rejected). `ExcludeDirectories`
lists directory names skipped anywhere in the tree — `.obsidian`, `.trash`,
`.git` are the common cases. `AllowRecreateOnConfigMismatch` permits dropping a
collection whose build configuration changed; `AllowSourceScopeChange` permits
reindexing when the source scope — root path, excluded directories or file
extensions — changed, which otherwise blocks to avoid mass insert or deletion.

## Building a standalone binary

`build-minerva-markdown-indexer-cli.sh` (in scripts folder) publishes a self-contained
binary to `bin-minerva-markdown-indexer/`:

```bash
./build-minerva-markdown-indexer-cli.sh
```

Output:

- `bin-minerva-markdown-indexer/minerva-markdown-indexer` — the executable
- `bin-minerva-markdown-indexer/appsettings.json` — copied from the project
  (`CopyToOutputDirectory=PreserveNewest` in the csproj keeps it current)

Run it with:

```bash
./bin-minerva-markdown-indexer/minerva-markdown-indexer
```

For long-running ingestions, detach it from the terminal:

```bash
nohup ./bin-minerva-markdown-indexer/minerva-markdown-indexer > logs/run.log 2>&1 &
```

## Overriding configuration

`Program.cs` builds configuration in this order (later sources override
earlier):

1. `appsettings.json` (required)
2. `appsettings.{DOTNET_ENVIRONMENT}.json` (optional, defaults to `Production`)
3. Environment variables
4. Command-line args

### Named profile files (recommended for experiments)

Drop additional files next to the binary:

```text
bin-minerva-markdown-indexer/
  minerva-markdown-indexer
  appsettings.json              # base / defaults
  appsettings.experiment-a.json # only the keys to override
  appsettings.experiment-b.json
```

Launch with the matching environment name:

```bash
DOTNET_ENVIRONMENT=experiment-a ./bin-minerva-markdown-indexer/minerva-markdown-indexer
```

The profile is merged on top of `appsettings.json`, so it only needs the keys
that differ. Add new profile files to `src/Minerva.MarkdownIndexer/`; the
`appsettings*.json` glob in the csproj copies them on each build.

The files have to sit next to `minerva-markdown-indexer` — they are loaded from
`AppContext.BaseDirectory`.

### Environment variables (one-off tweaks)

Use `__` (double underscore) as the section separator:

```bash
Indexer__RootPath=/path/to/notes \
Indexer__CollectionName=experiment-a \
./bin-minerva-markdown-indexer/minerva-markdown-indexer
```

### Command-line args

```bash
./bin-minerva-markdown-indexer/minerva-markdown-indexer --Indexer:RootPath=/path/to/notes --Indexer:CollectionName=experiment-a
```

## Files

| File | Role |
| --------------------------- | ------------------------------------------------------------------------------------ |
| `Program.cs` | Entry point — builds config, constructs the engine + indexer, runs one ingest pass |
| `IndexerOptions.cs` | Config record bound to the `Indexer` section |
| `MarkdownScanner.cs` | Enumerates files, parses frontmatter, extracts image attachments, derives `SourceId` |
| `MarkdownIndexer.cs` | Drives a single scan-and-ingest pass against `IMinervaEngine` |
| `MarkdownIndexerBuilder.cs` | Validation + preflight + construction of `MarkdownIndexer` |
