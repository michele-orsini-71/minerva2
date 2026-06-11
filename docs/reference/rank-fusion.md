# Hybrid search and Reciprocal Rank Fusion

How the two retrieval legs (dense vector + full-text) are combined into one
ranked list. The fusion logic is **Reciprocal Rank Fusion** (RRF; Cormack et
al. 2009), with a few project-specific twists. Code lives in
`src/Minerva/Search/SearchPipeline.cs` and `RankFusion.cs`.

## Pipeline, end to end

1. **Query setup** (`SearchPipeline.SearchAsync`). The query is embedded once
   and reused across all collections. Per collection, vector and full-text
   search run **in parallel**, producing two ranked lists.
2. **Per-collection candidate pool** (`SearchCollectionAsync`).
   ```
   candidatePoolSize = TopK * CandidatePoolMultiplier   // default 10 * 5 = 50
   ```
   Both legs are asked for `candidatePoolSize` results. The pool is
   intentionally larger than `TopK` because RRF needs **overlap** between the
   two lists to do anything: fetch only 10 from each and the chance both legs
   return the same chunk is small, so fusion degenerates to
   "vector-with-FTS-tiebreaks". Oversampling gives RRF room to reward chunks
   that appear in both lists.
3. **Rank fusion** (`RankFusion.Fuse`).
   ```
   score(chunk) = alpha       * 1 / (k + vectorRank)
                + (1 - alpha) * 1 / (k + ftsRank)
   ```
   with `k = 60` (standard RRF constant) and `alpha = HybridAlpha`
   (default 0.5). The fused set is the union of both pools, scored, sorted
   descending.
4. **Per-collection dedupe by source.** The fused list is walked top-down and
   only the first chunk per `SourceId` is kept; since the list is already
   sorted, this keeps the highest-scoring chunk per source and stops one
   document from monopolizing the top. Then `Take(TopK)`.
5. **Cross-collection merge.** Each collection's deduped top-K lists are
   concatenated, re-sorted by the same fused score, and trimmed to the global
   `TopK`. RRF is not re-applied across collections — the values are already
   fused scores, so re-ranking would be meaningless.
6. **Optional context expansion.** If `ContextRadius > 0`, `ContextExpander`
   pulls neighboring chunks around each hit. This enriches the payload; it
   does not affect ranking.

## Why the formula behaves as it does

- **Ranks, not scores.** Cosine distances and `ts_rank` scores live on
  different scales; comparing them numerically is meaningless. RRF discards
  raw scores and uses only the **position** (1, 2, 3, …), making it
  scale-invariant.
- **`1 / (k + rank)`.** The `+ k` (= 60) flattens the curve so rank 1 does not
  dominate everything; the gap between rank 1 and rank 2 stays modest.
- **Missing-list handling.** A chunk found by only one leg gets
  `missingRank = max(vec.Count, fts.Count) + 1` in the other — the worst
  position, but only one step worse than "last". So *absent ≈ last*, a mild
  penalty on small lists.
- **The `alpha` knob.** `1.0` → pure vector, `0.0` → pure full-text, `0.5` →
  equal weight. A chunk ranked #1 in both lists wins decisively.

`CandidatePoolMultiplier` (`SearchOptions.cs`) is the lever for step 2. Too
low and RRF has no overlap to reward; too high and DB work is wasted. The
warning in `SearchPipeline` fires when fewer than `TopK` results survive
dedupe — usually a sign the multiplier is too low for the corpus's
source-to-chunk ratio.

## Worked example — the role of k

Two ranked lists of 10 items each, `alpha = 0.5`:

```
vector: ['2','3','4','1','5','6','12','8','9','10']
fts:    ['1','8','2','3','4','15','6','7','9','10']
```

With **k = 60** the reciprocal-rank values are dominated by k (the lists are
small), so scores cluster tightly. Compare two chunks:

- id 5 — rank 5 (vector) + missing (rank 11 in fts):
  `1/(60+5) + 1/(60+11) = 0.01538 + 0.01408 = 0.02946`
- id 9 — rank 9 in **both**:
  `1/(60+9) + 1/(60+9) = 0.02898`

So id 5 (one strong showing, one absence) edges out id 9 (mediocre in both):
the gap `1/65 − 1/69 ≈ 0.0009` beats the missing-vs-rank-9 gap
`1/71 − 1/69 ≈ 0.0004`. With **k = 5** the same ordering holds but the score
spread widens sharply (rank 1 ≈ 0.146 versus ≈ 0.016 at k = 60).

**Takeaway:** RRF does not strictly enforce "consensus always wins." It
rewards consensus *and* a strong single-list signal, weighted by k. A large k
flattens the curve so a single strong rank can outweigh a mediocre appearance
in both lists.
