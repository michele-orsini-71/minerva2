# Storage footprint — how much the index inflates the source

Empirical measurement (2026-08-30, post-BM25 migration) of how much disk a
searchable collection costs relative to the original markdown, on the real
eval corpus. Conclusion: end to end a collection is ~**17× the source-text
size**, and **~80% of that is the dense vectors and their index** — the BM25
index costs ~0.6× the source, the contextual prefix ~2–3%. See
[contextualization-cost.md](contextualization-cost.md) for the *time* cost.

Supersedes the 2026-05-07 measurement on the `test-1` vault collection
(~7 MB source, ~20×, fts_vector era); same orders of magnitude.

## Where the cost lives

Per collection (`wikipedia-nollm`, no contextualization, 134,815 chunks,
~114 MB of source markdown):

| Layer | Size | Ratio vs. source |
| --- | --- | --- |
| Original markdown | ~114 MB | 1× |
| Stored `content` column | 94 MB | ~0.8× |
| Embedding column | 527 MB | ~4.6× |
| Row total (one collection, no indexes) | 818 MB | ~7× |
| pgvector HNSW index (this collection) | 1052 MB | ~9× |
| BM25 index share (see below) | ~70 MB | ~0.6× |
| Per-collection total incl. index shares | ~2.0 GB | **~17×** |

The embedding column plus its HNSW index is ~79% of the total; the HNSW index
alone is ~2× the embedding column it indexes. The BM25 index (`pg_search`) is
table-wide — one index over every collection's `content` — so a collection's
share is proportional to its content bytes: 148 MB across ~197 MB of total
content ≈ 0.75× the indexed text. That is far larger than the FTS-era GIN
index but still a rounding error next to the vectors.

The contextual prefix column measures 59 MB on `wikipedia-qwen2-5` (~0.5× of
content) — but that figure is inflated by the known runaway-prefix corruption
in that collection and will shrink after the contextualizer fix and
re-ingest.

## Measuring per-collection size

`pg_column_size` returns the on-disk size **after** TOAST compression, so these
are real costs, not raw text length. Postgres only compresses values larger than
~2 KB; chunks average ~730 B, so `content` is stored inline, uncompressed —
which is why stored `content` ≈ source size rather than smaller.

Per-collection column breakdown (also in
`src/Minerva/sql-scripts/Per-collection size and column breakdown.sql`):

```sql
SELECT
    collection_name,
    COUNT(*)                                                AS chunks,
    pg_size_pretty(SUM(pg_column_size(content)))            AS content_bytes,
    pg_size_pretty(SUM(pg_column_size(contextual_prefix)))  AS prefix_bytes,
    pg_size_pretty(SUM(pg_column_size(embedding)))          AS embedding_bytes,
    pg_size_pretty(SUM(pg_column_size(c.*)))                AS row_total_bytes
FROM chunks c
GROUP BY collection_name
ORDER BY collection_name;
```

Whole-table size (Postgres tracks storage per table, not per collection):

```sql
SELECT
    pg_size_pretty(pg_relation_size('chunks'))         AS table_only,
    pg_size_pretty(pg_indexes_size('chunks'))          AS indexes,
    pg_size_pretty(pg_total_relation_size('chunks'))   AS total_with_indexes_and_toast;
```

Index sizes (per-collection HNSW indexes, the table-wide BM25 index, and the
adjacency/pkey indexes):

```sql
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid)) AS size
FROM pg_stat_user_indexes
WHERE relname = 'chunks'
ORDER BY pg_relation_size(indexrelid) DESC;
```

**Caveat when reading whole-table totals**: they span every collection in the
database (4.6 GB total across the four live collections on 2026-08-30). The
per-collection table above apportions the shared indexes (BM25, pkey,
adjacency) by row/content share.

## Comparing against the original text

`du -sk` reports allocated filesystem blocks, not content bytes — with thousands
of small files on a 4 KB-block filesystem it over-reports, and it includes
non-markdown files. Sum actual markdown bytes instead:

```bash
find eval/collections/wikipedia-en-corpus -name "*.md" -type f -exec wc -c {} + | tail -1
```

Note that `SUM(content)` ≠ source text: chunk overlap (default 200 chars) adds
redundancy, while frontmatter and fence markers are partly normalized away
during ingestion — the two roughly cancel.

## Implication

The price of semantic search is the vector and its index, not the text or the
lexical index: switching the lexical leg from FTS (tsvector column + GIN) to
BM25 (`pg_search`) moved the lexical share from ~1.5× to ~0.6× of source and
either way it is dwarfed by HNSW. Reducing the storage footprint means a
smaller embedding dimension or a more compact vector index — trimming the
contextual prefix saves almost nothing on disk (it only helps ingestion
*time*).
