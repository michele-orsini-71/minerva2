# Changelog

All notable changes to Minerva (.NET, `minerva2`) are recorded here.

The format is based on [Keep a Changelog][kac]; this project follows
[Semantic Versioning][semver]. Versions advance independently of the Python
`minerva` (v3) — see the project naming note. While the major version is `0`,
the public surface may change at any time.

[kac]: https://keepachangelog.com/en/1.1.0/
[semver]: https://semver.org/spec/v2.0.0.html

## [Unreleased]

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
