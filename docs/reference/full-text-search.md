# Full-text search in Minerva

How the lexical (keyword) leg of hybrid search works. It uses PostgreSQL's
built-in full-text search end to end — no Lucene, no external BM25 engine.

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

Configuration: **`'simple'`** text-search config at both ingest and query
time. The corpus is mixed Italian/English and the lexical leg must match
codes and acronyms; `'simple'` is language-agnostic and does not stem or drop
such tokens.

Ingest builds the vector from chunk content (without the contextual prefix):

```sql
to_tsvector('simple', @fts_content)   -- PostgresChunkRepository, insert
```

Query relaxes a safely-parsed query to OR semantics:

```sql
WITH q AS (
    SELECT replace(websearch_to_tsquery('simple', @query)::text, ' & ', ' | ')::tsquery AS tsq
)
SELECT id, source_id, ..., ts_rank(fts_vector, q.tsq) AS rank
FROM chunks, q
WHERE collection_name = @coll AND fts_vector @@ q.tsq
ORDER BY rank DESC
LIMIT @topk
```

`websearch_to_tsquery` parses raw user input safely (punctuation, quoted
phrases, `or`, `-not`). Relaxing the top-level `&` operators to `|` gives
BM25-like **partial** matching: documents containing more of the query terms
rank higher via `ts_rank`. Phrase (`<->`) and negation operators are left
intact. A GIN index on `fts_vector` (migration `002_indexes.sql`) makes the
`@@` match fast.

## Is it TF-IDF or BM25?

Neither, strictly. PostgreSQL full-text search predates both and uses its own
scoring:

- **`ts_rank`** (what Minerva uses) is a frequency-based scorer, closer to
  TF-IDF than BM25 but with **no IDF** computed against the corpus. It weights
  by term frequency, optionally by document length, and by per-lexeme weights
  (A/B/C/D — unused here, so every lexeme is equal).
- **`ts_rank_cd`** is a cover-density variant that also rewards matches close
  together. Still not BM25.
- **Real BM25** is not in core PostgreSQL. It needs an extension (ParadeDB
  `pg_search`, VectorChord-bm25) or an external engine. This is tracked as a
  cross-phase backlog item on the roadmap.

In practice this matters less than it sounds: the fusion step uses only the
**ordinal rank** from the lexical leg, not `ts_rank`'s raw score, so fusion
papers over `ts_rank`'s weaknesses. A true BM25 would differ mainly on rare
terms and widely varying document lengths.

## The processing stack

| Layer | What Minerva uses |
| --- | --- |
| Tokenization | `to_tsvector('simple', …)` — split into lexemes, record positions; no stemming, no stopword removal |
| Query parsing | `websearch_to_tsquery('simple', …)`, top-level `&` relaxed to `\|` |
| Index | GIN on `fts_vector` (an inverted index: lexeme → rows containing it) |
| Match | the `@@` operator |
| Score | `ts_rank` — frequency-based, length-normalized, no IDF |

What `to_tsvector` does, conceptually `text → tsvector`: tokenize, normalize
(under `'simple'`: lowercase only — no stem, no stopword drop), and record
each lexeme's positions (positions feed phrase and proximity queries).

```sql
SELECT to_tsvector('simple', 'The quick brown foxes were running quickly');
-- 'brown':3 'foxes':4 'quick':2 'quickly':7 'running':6 'the':1 'were':5
```

Note that under `'simple'` nothing is stemmed or dropped — `foxes` stays
`foxes`, `the`/`were` are kept. Under `'english'` they would collapse
(`foxes → fox`, `running → run`) and stopwords would vanish.

## Trade-offs of `'simple'`

- **No stemming.** `russo` ≠ `russi`; inflected Italian will miss. Acceptable
  for the exact-token job; per-document language detection is the correct
  long-term fix (roadmap backlog).
- **Stopwords are indexed.** `the`, `il`, `la` become lexemes; index size
  grows slightly. A custom config (`simple` parser + a stopword dictionary,
  no stemmer) could drop them without stemming if it ever matters.
- **Ingest and query configs must match.** Mismatched configs are the classic
  silent-failure bug.

## History

Minerva originally used `to_tsvector('english', …)` +
`plainto_tsquery('english', …)`. On the mixed-language corpus this both stemmed
Italian by English rules and ANDed every query term (`plainto_tsquery` joins
tokens with AND), so a natural-language query against a 200-token chunk almost
never matched — the lexical leg returned ~0 hits and, through fusion, made
`hybrid_alpha` mathematically inert. The fix (`'simple'` config + OR-relaxed
`websearch_to_tsquery`, plus a one-time `fts_vector` rebuild of existing
collections) is recorded in
`.dev/roadmap-phase-1/completed/2026-06-07-fix-for-fts-reporting-0-hits.md`.
