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
The full story of how it was built, from the first
Python version to this one, is told in
[Minerva: implementation history](https://michele-orsini-71.github.io/posts/minerva-rag-system-implementation-history/).

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

[`eval/notebooks/runs_comparer.ipynb` (rendered on nbviewer)](https://nbviewer.org/github/michele-orsini-71/minerva2/blob/main/eval/notebooks/runs_comparer.ipynb) is
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

## License

MIT License — see the [LICENSE](LICENSE) file for details.

## Acknowledgments

Minerva builds upon these open-source projects:

- [PostgreSQL](https://www.postgresql.org/) — database
- [pgvector](https://github.com/pgvector/pgvector) — vector similarity search
- [ParadeDB `pg_search`](https://github.com/paradedb/paradedb) — BM25 keyword search
- [llama.cpp](https://github.com/ggml-org/llama.cpp) and
  [llama-swap](https://github.com/mostlygeek/llama-swap) — local model serving
- [Npgsql](https://www.npgsql.org/) — PostgreSQL driver for .NET
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) — MCP server
  framework
- [Markdig](https://github.com/xoofx/markdig) — markdown parsing

## About the Name

Minerva is named after the Roman goddess of wisdom, knowledge, and strategic
warfare — fitting for a system that helps manage and retrieve knowledge.
This project is dedicated to the memory of my mother, Nadia Minerva (Sept 30th,
1947 - Oct 17th, 2025).
