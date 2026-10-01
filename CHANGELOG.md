# Changelog

All notable changes to Minerva (.NET, `minerva2`) are recorded here.

The format is based on [Keep a Changelog][kac]; this project follows
[Semantic Versioning][semver]. Versions advance independently of the legacy
`minerva` (v3). While the major version is `0`,
the public surface may change at any time.

[kac]: https://keepachangelog.com/en/1.1.0/
[semver]: https://semver.org/spec/v2.0.0.html

## [Unreleased]

## [0.2.0] - 2026-10-01

### Added

- Cross-encoder reranker over the fused candidates (`Minerva.Reranker`,
  `Search.EnableReranker`, `Search.RerankDepth`), and an optional cascade
  reranker over the top of the reranked list (`Minerva.CascadeReranker`,
  `Search.CascadeDepth`; `0` turns it off). A reranker failure falls back to
  the fused ranking.
- MCP server (`Minerva.Mcp`, executable `minerva-mcp`) with the tools
  `list_collections`, `search`, `get_source`, `get_source_info` and
  `expand_chunk`.
- `sources` table: one row per ingested document, holding its content hash
  and full text, used by `get_source` and `get_source_info`.
- Collection `Description`, set by the indexer and read by MCP clients to
  choose a collection.
- Startup checks for the `pg_search` extension and for the pg_search 0.25.5
  bitmap intersection bug.
- Retry and bail-out when the model server is unavailable during ingestion;
  the indexer exits with a distinct code so the run can be resumed.
- `docs/installation.md`: full setup on macOS with llama-swap serving the
  models on demand.
- Evaluation: Success@K metric, `wp1283` Wikipedia corpus with distractor
  articles, experiment sweeps and analysis notebooks, comparison with the
  Python Minerva v1.

### Changed

- The keyword leg of hybrid search uses BM25 via ParadeDB `pg_search`
  instead of PostgreSQL built-in full-text search.
- Schema migrations squashed into `001_initial`; databases created by 0.1.0
  must be recreated and re-indexed.
- Every executable reads its config from `~/.config/minerva/<executable>.json`
  or `--config <path>`, plus an optional `<name>.<DOTNET_ENVIRONMENT>.json`
  overlay; it no longer reads config files next to the binary. When
  `DOTNET_ENVIRONMENT` is set, a missing overlay is an error.
- Search results carry `ChunkIndex`.

### Removed

- Contextual chunk prefixes (LLM-generated context before embedding): no
  retrieval gain measured on the full corpus, at a cost of 55-60 hours of
  ingestion.

## [0.1.0] - 2026-06-18

First tagged version. Marks the point where the evaluation harness and
collection provenance are complete, before the Wikipedia corpus ingest.

### Added

- Core retrieval pipeline: hybrid search (dense `bge-m3` embeddings plus
  Postgres full-text search), reciprocal-rank fusion, contextual chunk
  prefixes, structure-aware markdown chunking, and source-level dedupe.
- Markdown indexer (`Minerva.MarkdownIndexer`): scan, chunk, contextualize,
  and ingest a markdown vault into a Postgres + pgvector collection.
- Evaluation harness (`Minerva.Search.Bench`, assembly `minerva-bench`) with
  the `run`, `validate-dataset`, and `author-dataset` verbs; document-level
  ground truth and Recall@5/10/20 + MRR@10 metrics.
- Collection provenance (`collection_metadata`, migration `003`): typed
  `CollectionProvenance`, the reingest guard, and bench stamping of
  provenance into each run manifest.
- `--version` / `-v` flag on each shippable CLI, reporting the build version
  stamped as `<VersionPrefix>+<git-sha>`.
- `Minerva.MarkdownIndexer`: `FileExtensions` option. The indexer selects files
  by a configurable set of extensions (case- and dot-insensitive) instead of a
  hardcoded `*.md` glob. Replaces the previously-ignored `FilePattern` key.
- Source-scope change detection: the indexer records root path, excluded
  directories, and file extensions in collection provenance and refuses to
  reindex when they change, unless `AllowSourceScopeChange` is set. Excluded
  directories and extensions are compared as sets, so reordering is not a change.
  A blocked reindex throws `CollectionScopeChangeNotAllowedException`.
- `ClientProvenance` validates its own content: values must be scalars or flat
  arrays of scalars, enforced in the constructor (throws
  `ClientProvenanceException`). Reading collection metadata normalizes JSON
  values back to plain CLR scalars instead of leaking `JsonElement`.
