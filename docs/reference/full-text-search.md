# Full-text search in Minerva

How the lexical (keyword) leg of hybrid search works. Since 2026-08-30 it is
real BM25 via the [ParadeDB `pg_search`](https://github.com/paradedb/paradedb)
extension — no Lucene, no external search engine. The previous implementation
(PostgreSQL built-in full-text search: `tsvector`, `ts_rank`, GIN) is retired;
its last measured results are the `baseline-fts` runs in
`eval/experiments/baseline`.

## What full-text search is for

The lexical leg earns its keep on queries that embeddings handle poorly:

- **Identifiers** — error codes, ticket IDs, SKUs, function/class names, file
  paths, hashes, acronyms (`LRGB`, `IRPEF`, `BARNARD 22`, `Hα`).
- **Rare proper nouns** the embedding model never saw.
- **Verbatim phrases** the user remembers.
- **Negation / structure** embeddings miss ("without X", "before 2024").

For conceptual or paraphrased queries, embeddings dominate and the lexical
leg mostly adds noise that rank fusion damps. So its value scales with how
much query traffic is exact-token recall versus conceptual recall.

## Current implementation

The index is declared in migration `004_pg_search_extension.sql`:

```sql
CREATE INDEX chunks_bm25_idx ON chunks
USING bm25 (
    id,
    (content::pdb.simple('ascii_folding=true'))
) WITH (key_field = 'id');
```

- **`pdb.simple`** tokenizer: split on whitespace/punctuation, lowercase. No
  stemmer, no stop-word filter — the corpus is mixed Italian/English and the
  leg must match codes and acronyms, so the config stays language-neutral
  (the same reasoning that chose `'simple'` in the FTS era). Unlike Postgres
  FTS language configs, pg_search unbundles tokenization from filters: the
  one filter enabled is `ascii_folding`, so `perché` matches `perche`.
- **Stop words need no filter under BM25**: a term present in most documents
  gets a near-zero IDF weight automatically. (This was a real weakness of
  `ts_rank`, which has no IDF.)
- The index covers `content` only — the raw chunk text, without the
  contextual prefix. Indexing the prefix is deferred to the
  contextualization re-ingest, and will need a combined-text column
  (pg_search indexes columns, not expressions).
- Postgres maintains the index inside the same INSERT that writes the chunk;
  ingestion has no lexical-indexing step of its own.

Query (`PostgresChunkRepository.FullTextSearchAsync`):

```sql
SELECT id, ..., pdb.score(id) AS rank
FROM chunks
WHERE collection_name = @coll AND content ||| @query
ORDER BY rank DESC
LIMIT @topk
```

`|||` tokenizes the query with the same config as the index and matches
**any** term (ranked OR): documents containing more of the query's terms rank
higher, weighted by IDF. This replaces the FTS-era trick of relaxing
`websearch_to_tsquery`'s `&` operators to `|`. `pdb.score` is the BM25 score —
an unbounded positive number (not 0–1 like `ts_rank`). Rank fusion is
RRF and consumes list positions only, so the score scale is irrelevant
downstream; the score rides along in `ChunkSearchRecord.Distance` for
inspection.

## Decision record: fuzzy matching rejected as a default

pg_search supports typo-tolerant matching (`content ||| @query::pdb.fuzzy(1)`,
max Levenshtein distance 2). Applying it to every query was tried on
2026-08-30 and rejected:

- **It is not BM25.** Fuzzy matches are scored by roughly counting matched
  terms (scores come out quantized in 0.5 steps), not by TF/IDF. The best
  "match the most one-edit neighborhoods" chunk is a long, vocabulary-rich
  chunk, not a relevant one.
- **Common words explode.** Every short query word matches a large one-edit
  neighborhood, flooding the leg with noise.
- Measured effect: R@5 at `hybrid_alpha = 0.3` collapsed from .87 to .13 on
  the wikipedia dataset; gold chunks fell out of the candidate pool entirely.

If fuzzy comes back, it must be surgical — applied only to short,
identifier-like queries where typo tolerance is worth the precision loss —
never as a blanket cast on natural-language queries.

## Measured behavior (baseline experiment, wikipedia-nollm)

Versus the retired FTS leg, same collection and dataset (84 queries):

- `alpha 0.3`: R@5 .71 → .87, MRR@10 .60 → .75 — the lexical-heavy corner
  stopped collapsing, flattening the alpha curve.
- `alpha 0.5 / 0.7`: parity or slightly better.

Two low-alpha failure modes remain on queries deliberately crafted to defeat
lexical search; neither is a bug:

1. **Gold outside the lexical pool** — the query shares no informative token
  with the gold chunk, so gold takes RRF's missing-rank penalty
  (`The_Beatles.md-2`: "Coleoptera" retrieves Beetle.md, as designed).
2. **Organized competition** — gold is in the pool, but topically adjacent
  chunks match both lexically and semantically and RRF rewards the cross-leg
  agreement (`A_Fistful_of_Dollars.md-1`: Spaghetti_Western.md and
  Sam_Peckinpah.md outrank the gold film).

A stronger lexical leg converts the old leg's harmless noise into coherent
competitors on such queries; arbitrating them is the reranker's job — it
reads the content.

## Operational notes

- `pg_search` must be preloaded (`shared_preload_libraries`) and created by a
  superuser — see [installation.md](installation.md).
- Since v0.25 the extension requires `pgvector` to be installed first (it
  uses its vector type); Minerva satisfies this via migration order.
- The BM25 index is table-wide: one index serves every collection, and
  `CREATE INDEX` over existing rows is what "re-indexes" old collections —
  no re-ingestion needed when only the lexical leg changes.
