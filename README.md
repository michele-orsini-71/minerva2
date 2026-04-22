# Minerva

A C# / .NET 10 RAG engine backed by PostgreSQL + pgvector, with hybrid search (dense vectors + full-text with rank fusion), optional contextual preprocessing, and multiple collections.

This repo is a monorepo: the core library lives in `src/Minerva/`, and client applications that consume it live as sibling projects under `src/`.

## Layout

```
src/
  Minerva/                    Core library — engine, storage, ingestion, search, providers
  Minerva.MarkdownWatcher/    Filesystem watcher client that ingests markdown files
tests/
  Minerva.Tests/              Unit tests
  Minerva.IntegrationTests/   Integration tests (require PostgreSQL + pgvector)
scripts/
  run-integration-tests.sh    Spin up a test DB and run integration tests
```

See the per-project READMEs for details:
- [`src/Minerva/README.md`](src/Minerva/README.md) — core library reference
- [`src/Minerva.MarkdownWatcher/README.md`](src/Minerva.MarkdownWatcher/README.md) — markdown watcher client

## Prerequisites

- .NET SDK 10.0.101+ (pinned via `global.json`)
- PostgreSQL 16+ with the `pgvector` extension (for running the library, not for unit tests)
- An OpenAI-compatible embedding endpoint (Ollama, OpenAI, Together, etc.)
- Optional: an OpenAI-compatible chat endpoint if summarization or contextualization is enabled

## Build & test

```bash
dotnet build Minerva.sln
dotnet test                             # runs all tests — integration tests need a local Postgres
```

Integration tests read their connection string from `MINERVA_TEST_CONNSTRING`. A default
(`Host=localhost;Database=minerva_test;Username=postgres;Password=postgres`) is wired up
via `tests/Minerva.IntegrationTests/.runsettings`, which `dotnet test` picks up
automatically. To override (e.g. different local credentials, or in CI), set the env
var in your shell — a process-level env var takes precedence over `.runsettings`.

To bootstrap the test database (creates `minerva_test` with `pgvector` installed), run
`./scripts/run-integration-tests.sh` once after starting Postgres.

## Roadmap

The library is client-agnostic. The markdown watcher is the first client; future clients planned include an MCP server (`Minerva.Mcp`), a Claude conversation archiver, and an Obsidian-specific extension (`Minerva.Obsidian`) for wikilinks, embeds, tags, and dataview.
