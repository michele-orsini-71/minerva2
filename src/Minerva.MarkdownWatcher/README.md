# Minerva.MarkdownWatcher

A filesystem watcher that keeps a Minerva collection in sync with a directory of markdown files. The first real client of the [Minerva](../Minerva/README.md) library.

Works for:
- Obsidian vaults
- Repository `docs/` folders
- Static-site sources (Jekyll, Hugo, Astro, Quartz, …)
- Any directory of `.md` files with optional YAML frontmatter

Obsidian-specific features (wikilinks, embeds, tags, dataview) are **out of scope** — those would belong in a future `Minerva.Obsidian` extension layered on top.

## What it does

On startup:
1. Ensures the target Minerva collection exists (auto-creates it, probing the embedder for its vector dimension).
2. Scans the root directory and ingests every matching file.
3. Starts a `FileSystemWatcher` with debouncing; thereafter each create/change/delete/rename is reflected in the collection.

Re-ingestion is cheap because Minerva dedupes by chunk content hash — unchanged chunks are skipped.

Frontmatter is parsed and passed through as document `Metadata`. Markdown images (`![alt](path)`) are extracted into `AttachmentDescription`s using the alt text as the description.

## Usage

```bash
dotnet run --project src/Minerva.MarkdownWatcher
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
      "Model": "nomic-embed-text"
    }
  },
  "Watcher": {
    "RootPath": "/path/to/markdown/root",
    "CollectionName": "my-notes",
    "FilePattern": "*.md",
    "DebounceMs": 500,
    "ExcludeDirectories": [".obsidian", ".trash", ".git"]
  }
}
```

The `Minerva` section is the full core-library config (see [`src/Minerva/README.md`](../Minerva/README.md)); the `Watcher` section configures this client.

`ExcludeDirectories` defaults cover the common cases (Obsidian internals + git metadata). Override it for other workflows.

> **Local-runtime tip.** If you point `Minerva.Embedding` and `Minerva.Llm` at the same local runtime (Ollama, LM Studio, …), keep both models resident — otherwise the watcher will trigger model swaps in and out of VRAM whenever it alternates between embedding and summarization/contextualization.
>
> - **Ollama**: set `OLLAMA_MAX_LOADED_MODELS=2` (or higher) and a generous `OLLAMA_KEEP_ALIVE` (e.g. `24h`).
> - **LM Studio**: load both models in the *Models* panel before starting the watcher.

## Files

| File | Role |
|---|---|
| `Program.cs` | Host builder — wires `AddMinerva` + `AddMinervaWatcher` |
| `WatcherOptions.cs` | Config record bound to the `Watcher` section |
| `MarkdownScanner.cs` | Enumerates files, parses frontmatter, extracts image attachments, derives `SourceId` |
| `MarkdownSyncService.cs` | `BackgroundService` — initial scan + `FileSystemWatcher` + ingest |
| `PathDebouncer.cs` | Coalesces rapid repeated events for the same path |
| `DI/ServiceCollectionExtensions.cs` | `AddMinervaWatcher()` extension |
