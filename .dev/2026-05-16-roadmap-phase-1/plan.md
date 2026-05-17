# First client for Minerva Search

The ingestion side has been built and stress-tested; the search side has never run. The first client's job is to exercise it end-to-end with a real corpus, expose the tuning knobs, and surface the actual retrieval quality so we can
form an opinion on defaults.

## Decisions

### Two separate projects, CLI first

- **`src/Minerva.Search.Cli/`** (this iteration) — single-shot command-line tool. Take a query + collection + params, print results, exit. Manual re-invocation lets you compare different parameter values for the same query side-by-side in the terminal.
- **`src/Minerva.Search.Bench/`** (later iteration) — automated test bench. Reads a queries file and a sweep config (TopK × HybridAlpha grid), runs the matrix, emits CSV with metrics (Recall@K, MRR if expected chunks are provided). The CLI is the spike; the bench is the harness.

Kept as separate projects rather than one binary with subcommands, per user preference for separation.

### CLI surface (v1)

```text
minerva-search <query> --collection <name>
               [--top-k 10]
               [--alpha 0.5]
               [--expand-context]
               [--format table|json]
               [--full] [--snippet-chars 200]
```

- `--collection` is **required** and **single-valued** (no implicit default
  from config, not repeatable). Picking the corpus is the most important
  question; a silent default invites mistakes. Searching across multiple
  corpora at once is intentionally not supported — RRF, the candidate
  pool sizes, and the eval harness are all tuned per-collection, and
  cross-collection score merging is statistically incoherent. If multi-
  corpus retrieval is ever needed, it belongs above this layer (host
  fans out, reranks the union).
- Exit codes: `0` results found, `3` zero results, `2` bad args / startup,
  `1` unhandled.
- Default log level `Warning` (not `Information` like the indexer) so
  pipeline chatter doesn't mix with results in an interactive session.

## Deferred / discarded

### LLM in the retrieval path — not needed

`SearchAsync` already takes a plain string; the vector side handles
natural-language phrasing via embeddings, the FTS side handles exact tokens,
and `RankFusion` combines them. An LLM would add value only as a *layer on
top* of retrieval (query rewriting, HyDE, reranking, RAG answer synthesis) —
not for the retrieval itself. An eventual MCP server exposes `SearchAsync` to
an external LLM (the LLM lives client-side); Minerva itself stays
LLM-free.

### REPL mode — postponed

Single-shot is enough to iterate on params by re-invocation. REPL adds state
management and prompt UX without changing what's being tested. Add later if
the friction of re-invocation becomes real.

### `--metadata-filter` — postponed

Three reasons:

1. The filter execution path may not yet be honored by `VectorSearch` /
   `FullTextSearch`. Adding the flag before verifying would be a lie.
2. CLI `key=value` parsing is type-ambiguous (`year=2024` int or string?
   `published=true` bool or string?). Postgres JSONB cares.
3. It would expand the bench's option space (per-query filters), inflating
   the design before there's a need.

Revisit when there's a concrete reason to filter (e.g. "search only the 2024
folder").

### Shared appsettings file — postponed

A common `minerva.shared.json` loaded by all consumers would avoid
duplication, but every implementation has a wart (relative path traversal,
copy-at-build, env-var indirection). Not worth it for two consumers. The env
variable override path (`Minerva__ConnectionString`, etc.) is already
available via `.AddEnvironmentVariables()` for anyone who wants to centralize
secrets that way.

### UI client — discarded for now

A graphical browser would be nicer for scrolling/expanding chunks, but
slower to build and adds noise. Console first; graduate to UI only if the
need is proven.

## Glossary

- **HybridAlpha** — fusion weight in `RankFusion.Fuse`. Score is `α · 1/(k+vectorRank) + (1−α) · 1/(k+ftsRank)` (Reciprocal Rank Fusion, k=60). `α=1.0` pure vector, `α=0.0` pure full-text, `α=0.5` balanced. Optimal value is corpus- and query-dependent — that's why it's a knob.
