# Storage footprint — how much the index inflates the source

Empirical measurement (2026-05-07) of how much disk a searchable collection
costs relative to the original markdown. Conclusion: end to end a collection is
~**20× the source-text size**, and **~95% of that is the dense vectors and
their index** — contextualization adds ~1.5%, expensive only to *create*, not
to store. See [contextualization-cost.md](contextualization-cost.md) for the
*time* cost.

## Where the cost lives

Per collection (`test-1`, contextualized, 8886 chunks, ~7 MB of source
markdown):

| Layer | Size | Ratio vs. source |
|---|---|---|
| Original markdown | ~7 MB | 1× |
| Stored `content` column | 6.4 MB | ~0.9× |
| `contextual_prefix` column | 2.2 MB | ~0.3× |
| Embedding column | 35 MB | ~5× |
| `fts_vector` column | 9.4 MB | ~1.3× |
| Row total (one collection, no indexes) | 55 MB | ~8× |
| Per-collection total incl. indexes | ~140 MB | **~20×** |

The embedding column dominates the row (~64%); the gap from row totals to the
full table is **indexes** — mostly the pgvector HNSW index (typically 1.5–2× the
embedding column) plus the GIN index on `fts_vector`. The contextual prefix is
~2 MB out of 140 MB: negligible as storage, even though it is ~40% of a chunk's
text at the *prompt* level (which is what makes ingestion slow).

## Measuring per-collection size

`pg_column_size` returns the on-disk size **after** TOAST compression, so these
are real costs, not raw text length. Postgres only compresses values larger than
~2 KB; chunks average ~740 B, so `content` is stored inline, uncompressed —
which is why stored `content` ≈ source size rather than smaller.

Per-collection column breakdown:

```sql
SELECT
    collection_name,
    COUNT(*)                                                AS chunks,
    pg_size_pretty(SUM(pg_column_size(content)))            AS content_bytes,
    pg_size_pretty(SUM(pg_column_size(contextual_prefix)))  AS prefix_bytes,
    pg_size_pretty(SUM(pg_column_size(embedding)))          AS embedding_bytes,
    pg_size_pretty(SUM(pg_column_size(fts_vector)))         AS fts_bytes,
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

Index sizes (to see the HNSW and GIN contribution):

```sql
SELECT indexrelname, pg_size_pretty(pg_relation_size(indexrelid)) AS size
FROM pg_stat_user_indexes
WHERE relname = 'chunks'
ORDER BY pg_relation_size(indexrelid) DESC;
```

## Comparing against the original text

`du -sk` reports allocated filesystem blocks, not content bytes — with thousands
of small notes on a 4 KB-block filesystem it over-reports by several MB of slack,
and it includes attachments. Sum actual markdown bytes instead:

```bash
find /path/to/notes -name "*.md" -type f -exec wc -c {} + | tail -1
```

Then compare that figure against `pg_total_relation_size('chunks')`, scaling by
row share for a single collection. Note that `SUM(content)` ≠ source text: chunk
overlap (default 200 chars) adds redundancy, while frontmatter and fence markers
are partly normalized away during ingestion — the two roughly cancel.

## Implication

The price of semantic search is the vector and its index, not the text or the
context prefix. Reducing the storage footprint means a smaller embedding
dimension or a more compact vector index — trimming the contextual prefix saves
almost nothing on disk (it only helps ingestion *time*).
