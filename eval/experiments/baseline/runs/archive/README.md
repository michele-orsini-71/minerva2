# Baselines archive

- 2026-08-27T08-43-19Z_wikipedia-v1, 2026-08-27T08-45-52Z_wikipedia-v1 - measurements
  of the first baseline, which used the PostgreSQL built-in full-text search engine
- 2026-08-30T08-59-15Z_wikipedia-v1 - the first BM25 measurement

Comparing them shows the improvement between the two full-text search methods.

- 2026-09-10T14-46-02Z_wp1283-v1 - baseline on the wp1283-nollm collection
  ingested 2026-09-05.
  Superseded by 2026-09-12T16-33-27Z_wp1283-v1, same config on the
  collection re-ingested 2026-09-12 (ingestor 5b9f72c, schema 001_initial).
  Comparing the two gives the re-ingestion noise floor: same chunks (all
  gold chunk ids identical), 23 of 252 cells change a metric, all by one
  or two rank positions of the gold chunk, max fused-score delta 0.007.
  Sources of the noise: HNSW (vector index) rebuild, recomputed embeddings,
  BM25 corpus statistics rebuilt on a different table state.
  Rule: only compare runs made on the same collection ingestion; a delta
  under this floor is not a result.

- 2026-09-12T16-33-27Z_wp1283-v1 - baseline on the 2026-09-12 re-ingestion of
  wp1283-nollm, scored against the dataset before the 2026-09-17 gold changes
  (Arthropod.md-2 query rewritten, Virus.md removed from HIV.md-1).
  Superseded by the first run made after those changes.
