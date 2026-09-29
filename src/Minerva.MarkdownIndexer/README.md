# Minerva.MarkdownIndexer

A command-line tool that syncs a Minerva collection with a directory of
markdown files, then exits. The first real client of the
[Minerva](../Minerva/README.md) library.

Works for:

- Obsidian vaults
- Repository `docs/` folders
- Static-site sources (Jekyll, Hugo, Astro, Quartz, …)
- Any directory of `.md` files with optional YAML frontmatter

Obsidian-specific features (wikilinks, embeds, tags, dataview) are **out of
scope** — those would belong in a future `Minerva.Obsidian` extension layered on
top.

## What it does

Each run:

1. Ensures the target Minerva collection exists (auto-creates it, probing the
   embedder for its vector dimension).
2. Scans the root directory and passes every matching file to Minerva.
3. Minerva adds new files, re-embeds changed ones, skips unchanged ones (same
   content hash), and removes the ones no longer on disk.
4. Logs a summary (`+added ~updated -deleted =unchanged !!failed`) and exits.

Run it again whenever the files change; later runs process only changed files.

Frontmatter is parsed and passed through as document `Metadata`. Markdown images
(`![alt](path)`) are extracted into `AttachmentDescription`s using the alt text
as the description.

## Usage

```bash
dotnet run --project src/Minerva.MarkdownIndexer -- --config path/to/config.json
```

Exit codes: `0` success or cancelled, `1` unexpected error, `2` invalid
configuration or failed startup checks, `3` some documents failed (rerun to
retry them), `4` stopped after repeated model-server errors (rerun to continue).

## Configuration

The config file (`appsettings.json` in this folder is the example):

```json
{
  "Minerva": {
    "ConnectionString": "Host=localhost;Database=minerva;Username=minerva;Password=minerva",
    "Embedding": {
      "BaseUrl": "http://localhost:9930/v1",
      "Model": "text-embedding-bge-m3",
      "Concurrency": 1,
      "BatchSize": 1
    },
    "Chunking": {
      "TargetChunkSize": 1200,
      "ChunkOverlap": 200,
      "ChunkerType": "Custom"
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
clear error if any is missing. The optional `Logging:File:Path` key also writes
the log to a file (`~/` and `{Date}` are expanded).

`Description` tells MCP clients what the collection contains, so they can pick
it without being told its name; it is stored only when the collection is
created. `FileExtensions` lists the file types to index as bare extensions such
as `md` or `txt` (matching is case- and dot-insensitive, so `md`, `.md` and
`.MD` are equivalent; a glob like `*.md` is rejected). `ExcludeDirectories`
lists directory names skipped anywhere in the tree — `.obsidian`, `.trash`,
`.git` are the common cases. `AllowRecreateOnConfigMismatch` permits dropping a
collection whose build configuration changed; `AllowSourceScopeChange` permits
reindexing when the source scope — root path, excluded directories or file
extensions — changed, which otherwise blocks to avoid mass insert or deletion.

## Building a standalone binary

[`docs/installation.md`](../../docs/installation.md) publishes the executable
to `~/bin/minerva-markdown-indexer`. For a local build,
`scripts/build-minerva-markdown-indexer-cli.sh` publishes it to
`bin-minerva-markdown-indexer/`.

For long-running ingestions, detach it from the terminal:

```bash
nohup minerva-markdown-indexer > indexer.out 2>&1 &
```

## Overriding configuration

Configuration is built in this order (later sources override earlier):

1. The config file: `--config <path>`, otherwise
   `~/.config/minerva/minerva-markdown-indexer.json` (required). The current
   directory is never used.
2. `<config file name>.<DOTNET_ENVIRONMENT>.json` in the same folder, when
   `DOTNET_ENVIRONMENT` is set (optional).
3. Environment variables
4. Command-line args

### Named profile files (recommended for one collection each)

Put overlay files next to the config file:

```text
~/.config/minerva/
  minerva-markdown-indexer.json            # base / defaults
  minerva-markdown-indexer.my-notes.json   # only the keys to override
  minerva-markdown-indexer.work-docs.json
```

Launch with the matching environment name:

```bash
DOTNET_ENVIRONMENT=my-notes minerva-markdown-indexer
```

The overlay is merged on top of the base file, so it only needs the keys that
differ.

### Environment variables (one-off tweaks)

Use `__` (double underscore) as the section separator:

```bash
Indexer__RootPath=/path/to/notes \
Indexer__CollectionName=experiment-a \
minerva-markdown-indexer
```

### Command-line args

```bash
minerva-markdown-indexer --Indexer:RootPath=/path/to/notes --Indexer:CollectionName=experiment-a
```

## Files

| File | Role |
| --------------------------- | ------------------------------------------------------------------------------------ |
| `Program.cs` | Entry point — builds config, constructs the engine + indexer, runs one ingest pass |
| `IndexerOptions.cs` | Config record for the `Indexer` section |
| `IndexerOptionsBinder.cs` | Binds and validates the `Indexer` section |
| `MarkdownScanner.cs` | Enumerates files, parses frontmatter, extracts image attachments, derives `SourceId` |
| `MarkdownIndexer.cs` | Drives a single scan-and-ingest pass against `IMinervaEngine` |
| `MarkdownIndexerBuilder.cs` | Validation + preflight + construction of `MarkdownIndexer` |
