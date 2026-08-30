# Ingestion timing — three modes compared

Empirical measurement (2026-05-11) of ingestion wall-clock across three
collections built from the same corpus. Predates the BM25 migration
(2026-08-30): the "FTS-only" mode is now BM25 via `pg_search`, but the
conclusions are index-independent — lexical index maintenance sits in the
fast-path floor either way. Establishes that LLM contextualization,
not embedding or DB I/O, dominates ingestion cost — and that the cost is
concentrated in the fattest documents.

## What was measured

| collection | mode | docs |
| --- | --- | --- |
| `test-2` | FTS-only, no LLM | 1821 |
| `qwen2-5` | vector + small LLM (Qwen 2.5) | 1824 |
| `test-1` | vector + heavier LLM (Gemma 4 e4b) / slower path | 1811 |

**Method.** Each document's chunks share one `created_at` (one transaction per
document, `NOW()` = transaction start), so timing resolution is **per-document,
not per-chunk**. Sources are sorted by `created_at`; the gap to the previous
source approximates the time to process the next document. A handful of long
gaps are restart pauses and are negligible to the aggregate.

## Headline numbers

Active wall-clock, after splitting each distribution into a fast and a slow
bucket at the auto-detected valley:

| collection | fast docs | slow docs | fast median | slow median | active total |
| --- | --- | --- | --- | --- | --- |
| `test-2` (FTS-only) | 920 | 901 | 0.069 s | 0.33 s | ~600 s (10 min) |
| `qwen2-5` (small LLM) | 972 | 852 | 0.070 s | 5.32 s | ~38,500 s (10.7 h) |
| `test-1` (heavy LLM - Gemma 4 e4b) | 989 | 822 | 0.094 s | 99.63 s | ~188,700 s (52.4 h) |

## What the split shows

- **The fast-path floor is identical across all three modes** (~0.07–0.09 s
  median). That floor is DB I/O + the embedding API roundtrip; nothing
  mode-specific happens there.
- **Roughly half the corpus triggers the slow (LLM) path** in both `qwen2-5`
  and `test-1` (852/1824 and 822/1811, ~47%). This is a property of the
  corpus's document-size distribution, not the mode — and those same documents
  account for ~99% of the wall-clock.
- **`test-1`'s slow path is ~19× heavier per document than `qwen2-5`'s**
  (99.63 s vs 5.32 s) — the headline cost of the heavier model/path.
- **Adding the small LLM is free for small documents.** In the same-document
  scatter (`qwen2-5` vs `test-2`), single-chunk documents sit on y = x: the LLM
  never triggers. Documents that do trigger it pay a **3–500×** premium, and
  the premium *grows* with document size (log-log slope < 1): FTS indexing
  scales with text length, LLM contextualization scales with length *and*
  per-token latency.

## Implication

Optimization wins come from **skipping or down-sampling the LLM path on the
fattest documents**, not from speeding up the already-fast median document.
The mean is a misleading central estimator here (mean/median is 105× for
`qwen2-5`, 417× for `test-1`); read the histogram peaks and per-bucket medians,
not the mean.

**Caveats.** Per-chunk timing is not recoverable from this schema. Document
ordering differs across runs, so per-source gaps are partly sequence artefacts
— but the same-document scatter comparisons are reliable. Re-upserts wipe and
re-insert chunks, so `created_at` reflects only the latest run.

See [contextualization-cost.md](contextualization-cost.md) for *why* the LLM
path is slow, and [model-speedups.md](model-speedups.md) for the levers.
