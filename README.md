# Minerva

Minerva is a RAG engine for personal notes, based on a PostgreSQL
 database, built to run locally, without accessing the internet.
It is built on C# / .NET 10.
Semantic (`pgvector`) and keyword (ParadeDB `pg_search`) searches are merged with
 Reciprocal Rank Fusion and then rescored by a cross-encoder reranker.
All models are downloaded and run locally behind OpenAI-compatible
endpoints; indexed files are grouped in collections and then exposed to AI
assistants through an MCP server.

Minerva is the successor of
[legacy Minerva](https://github.com/michele-orsini-71/minerva), the original
Python implementation.

This is a monorepo: the core library lives in `src/Minerva/`, and the
applications that consume it live as sibling projects under `src/`.

## Layout

```text
src/
  Minerva/                    Core library — ingestion, search, storage, providers
  Minerva.MarkdownIndexer/    CLI: indexes a directory of markdown files into a collection
  Minerva.Search.Cli/         CLI: searches a collection (minerva-search)
  Minerva.Mcp/                MCP server exposing search to AI assistants (minerva-mcp)
  Minerva.Search.Bench/       Evaluation harness: runs retrieval benchmarks (minerva-bench)
  Minerva.Utils/              Helpers shared by the executables (config files, --version)
tests/
  Minerva.Tests/              Unit tests
  Minerva.IntegrationTests/   Integration tests (require PostgreSQL + extensions)
  Minerva.ArchitectureTests/  Clean Architecture layering rules
tools/
  Minerva.ChunkComparator/    Development tool: compares chunker outputs
eval/                         Eval corpora, datasets, collections and experiments
scripts/                      Build scripts, llama-swap service files
```

Per-project READMEs:

- [`src/Minerva/README.md`](src/Minerva/README.md) — core library
- [`src/Minerva.MarkdownIndexer/README.md`](src/Minerva.MarkdownIndexer/README.md)
  — markdown indexer
- [`tests/Minerva.ArchitectureTests/README.md`](tests/Minerva.ArchitectureTests/README.md)
  — layer model and dependency rules
- [`eval/README.md`](eval/README.md) — evaluation setup

## Results

[`eval/notebooks/runs_comparer.ipynb`](eval/notebooks/runs_comparer.ipynb) is
committed with its outputs: it compares legacy Minerva with Minerva, with and
without the reranker, without having to build the corpus and run the evals.

## Installation

Follow the instructions in [`docs/installation.md`](docs/installation.md) to set
up the whole stack on a Mac:

- PostgreSQL with `pgvector` and `pg_search`
- llama-swap serving the embedding and reranker models
- the executables
- the MCP clients

## Build & test

```bash
dotnet build Minerva.sln
dotnet test                             # runs all tests — integration tests need a local Postgres
```

Integration tests read their connection string from `MINERVA_TEST_CONNSTRING`. A
default
(`Host=localhost;Database=minerva_test;Username=postgres;Password=postgres`) is
wired up via `tests/Minerva.IntegrationTests/.runsettings`, which `dotnet test`
picks up automatically. To override (e.g. different local credentials, or in
CI), set the env var in your shell — a process-level env var takes precedence
over `.runsettings`.

To bootstrap the test database, run `./scripts/run-integration-tests.sh` once
after starting Postgres.

## Editor debug profiles

Per-project launch profiles live in `Properties/launchSettings.json`. These are
git-ignored, because they point at machine- and data-specific paths (local
collections, private datasets). Each project ships a tracked
`Properties/launchSettings.template.json` instead. After a fresh clone, copy the
template next to it and replace the `<placeholder>` values:

```bash
cp src/Minerva.Search.Bench/Properties/launchSettings.template.json \
   src/Minerva.Search.Bench/Properties/launchSettings.json
```

The same applies to `appsettings.debug.template.json`, copied to the
git-ignored `appsettings.debug.json`.
