---
slug: 2026-06-07-fix-for-fts-reporting-0-hits
created: 2026-06-07T00:00:00Z
last_updated: 2026-06-07T00:00:00Z
parent: 2026-05-18-eval-harness-implementation-phases.md
---

# Fix: full-text search returning ~0 hits

## How it surfaced

The first real sweep (Phase 1C, the personal-notes-v1 dataset, 30 queries ×
3 `hybrid_alpha` cells × 3 collections) produced two suspicious patterns:

- **`hybrid_alpha` had no effect at all** — the per-cell aggregates were
  byte-identical for `alpha ∈ {0.3, 0.5, 0.7}` within every collection.
- The lexical (BM25/FTS) branch appeared to contribute nothing.

This is the eval harness doing its job: it exposed a pipeline defect before any
Phase 2 tuning was attempted.

## Why alpha was inert

Fusion is a weighted Reciprocal Rank Fusion ([`RankFusion.cs`](../../src/Minerva/Search/RankFusion.cs)):

```text
score = alpha · 1/(k + vectorRank) + (1 - alpha) · 1/(k + ftsRank)
```

When the FTS branch returns an empty list, every chunk receives the *same*
"missing" rank, so the second term becomes an identical constant added to all
chunks and the first term is `alpha · (vector component)`. Ordering by
`alpha · V + C` with `alpha > 0` and constant `C` reproduces the pure vector
order **for any alpha**. So an empty FTS list does not merely weaken the lexical
branch — it makes `alpha` mathematically irrelevant.

## Root cause — two compounding bugs

Both were in the full-text SQL of
[`PostgresChunkRepository.cs`](../../src/Minerva/Storage/PostgresChunkRepository.cs).

### Bug A — wrong text-search configuration

Ingest built the vector with `to_tsvector('english', …)` and search used
`plainto_tsquery('english', …)`. The corpus is mixed Italian/English with an
Italian majority. The English analyzer:

- applies the wrong stemmer (Italian words stemmed by English rules), and
- removes English stop-words while **keeping Italian function words**
  ("di", "dal", "del", "che") as required content terms.

### Bug B — `plainto_tsquery` ANDs every term

`plainto_tsquery` joins all tokens with AND. A natural-language query becomes a
strict conjunction of every word, so a 200-token chunk almost never satisfies
it. This is not BM25 semantics: real BM25 ranks **partial** matches.

### Evidence (queries run against the live DB)

```sql
SELECT plainto_tsquery('english', 'dipendenza energetica europea dal gas russo');
-- 'dipendenza' & 'energetica' & 'europea' & 'dal' & 'gas' & 'russo'   (note "dal")

SELECT count(*) FROM chunks WHERE collection_name = 'test-1'
  AND fts_vector @@ plainto_tsquery('english', 'dipendenza energetica europea dal gas russo');
-- 0

SELECT count(*) FROM chunks WHERE collection_name = 'test-1'
  AND fts_vector @@ plainto_tsquery('english', 'gas');
-- 40+      (the GIN index itself works)

SELECT plainto_tsquery('italian', 'dipendenza energetica europea dal gas russo');
-- 'dipendent' & 'energet' & 'europe' & 'gas' & 'russ'   (drops "dal", stems properly)
```

A proof-of-fix query (`'simple'` config + OR of the terms, ranked by
`ts_rank`) surfaced the correct Draghi document at ranks 2, 3, 8, 9, 10 —
confirming that OR + `'simple'` recovers the lexical signal.

## Decision

Apply all three of the following:

1. **Rebuild** `fts_vector` for all existing collections (test-1, test-2,
   qwen2-5). The column is independent of the embeddings, so this is a pure
   text re-index — **no re-embedding or re-ingestion**.
2. **Use `'simple'`** as the text-search configuration going forward.
   Rationale: the corpus is mixed-language and the lexical branch must match
   codes/acronyms (`LRGB`, `BARNARD 22`, `IRPEF`). `'simple'` is
   language-agnostic and does not strip such tokens. Trade-off: no stemming
   (`russo` ≠ `russi`).
3. **OR-relax the query** so the lexical branch behaves like BM25 (partial
   matches ranked) rather than all-or-nothing AND.

`'simple'` over `'italian'`: a single Italian config would mis-handle the
English notes; per-document language detection is the correct long-term answer
but is deferred.

## Implementation

- **Ingest config** — [`PostgresChunkRepository.cs`](../../src/Minerva/Storage/PostgresChunkRepository.cs):
  `to_tsvector('english', …)` → `to_tsvector('simple', …)`.
- **Search query** — same file: replaced
  `plainto_tsquery('english', …)` with
  `replace(websearch_to_tsquery('simple', @query)::text, ' & ', ' | ')::tsquery`,
  computed once in a CTE. `websearch_to_tsquery` parses raw user input safely
  (punctuation, quoted phrases, `or`, `-not`); relaxing the top-level `&` to
  `|` gives partial-match recall. Phrase (`<->`) and negation operators are
  left intact.
- **Rebuild — manual, one-time, run in DBeaver** (per collection):

  ```sql
  UPDATE chunks SET fts_vector = to_tsvector('simple', content) WHERE collection_name = 'test-1';
  UPDATE chunks SET fts_vector = to_tsvector('simple', content) WHERE collection_name = 'test-2';
  UPDATE chunks SET fts_vector = to_tsvector('simple', content) WHERE collection_name = 'qwen2-5';
  ```

  If a collection is large enough to be slow, drop the GIN index, run the
  UPDATE, then recreate it:
  `DROP INDEX idx_chunks_fts;` … `CREATE INDEX idx_chunks_fts ON chunks USING GIN(fts_vector);`.

  **Why not an automatic migration.** The first attempt shipped this as
  `003_fts_simple.sql`. The embedded-migration runner wraps each migration in a
  transaction with a 30 s command timeout; a full-table `to_tsvector` rebuild
  exceeded it, rolled back, and — because the migration is only recorded on
  success — retried on every startup, blocking the tool entirely. Beyond the
  timeout, a heavy data backfill does not belong in startup migrations: a fresh
  install ingests with `'simple'` from the start and has nothing to rebuild, so
  this is a one-time fix for pre-existing data, not a permanent schema step.
  Keep startup migrations cheap and schema-only.

## Verification

1. Re-run the bench; `hybrid_alpha` should now change the per-cell aggregates.
2. With `Logging:LogLevel:Default=Debug`, the `… {Fts} fts …` line in
   [`SearchPipeline.cs`](../../src/Minerva/Search/SearchPipeline.cs) should show
   non-zero FTS counts.
3. Add a few keyword-only eval queries (`LRGB`, `IRPEF`, `BARNARD 22`) that the
   lexical branch should win and dense retrieval may miss.

## Follow-ups / caveats

- **`ts_rank` is not true BM25** (no IDF / length normalization in the same
  form). For genuine BM25 a Postgres extension (ParadeDB / `pg_search`,
  VectorChord-bm25) would be needed — out of scope now, noted for later.
- **Negation edge case:** a query like `a -foo` becomes `'a' | !'foo'` after
  the relax, which is semantically loose. Negation is rare in our queries;
  refine if it ever matters.
- **Mixed-language stemming** is unaddressed by `'simple'`; revisit with
  per-document language detection if recall on inflected Italian proves weak.
- This is a **search-pipeline fix, separate from the eval harness**. It gates
  any meaningful hybrid/alpha measurement in Phase 2 — record on the main
  roadmap.
