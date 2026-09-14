# Baselines archive

- 2026-08-27THH-MM-SSZ_wikipedia-v1 - these are meaurements of the first baseline
  that used postgre FTS engine
- 2026-08-30T08-59-15Z_wikipedia-v1 - this is the first BM25 measurment

comparing them will show the improvements between the two search text methods.

- 2026-09-10T14-46-02Z_wp1283-v1 - baseline on the wp1283-nollm collection
  ingested 2026-09-05. 
  Superseded by ../2026-09-12T16-33-27Z_wp1283-v1, same config on the
  collection re-ingested 2026-09-12 (ingestor 5b9f72c, schema 001_initial).
  Comparing the two gives the re-ingestion noise floor: same chunks (all
  gold chunk ids identical), 23 of 252 cells change a metric, all by one
  or two rank positions of the gold chunk, max fused-score delta 0.007.
  Sources of the noise: HNSW rebuild, recomputed embeddings, BM25 corpus
  statistics rebuilt on a different table state.
  Rule: only compare runs made on the same collection ingestion; a delta
  under this floor is not a result.

