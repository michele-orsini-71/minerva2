# Minerva RAG Pipeline — Working Design

A summary of the retrieval pipeline decisions discussed, with notes on how parameters should shift across implementation phases.

---

## Pipeline overview

```text
Query
  │
  ├──► Semantic search ──► top N₁ ─┐
  │                                ├──► RRF fusion ──► top N₂ ──► [Rerank] ──► top K ──► [Expand ±W, merge] ──► LLM
  └──► BM25 search ─────► top N₁ ─┘
```

Square brackets `[...]` mark stages that are **optional / added incrementally**.

---

## Stage details

### 1. Hybrid search on contextualized chunks

- Run **semantic** (vector) and **BM25** (lexical) searches in parallel on the same indexed corpus.
- Chunks target **200 tokens** (see chunking strategy below), each prepended with its Contextual Retrieval prefix (per the Anthropic article).
- Returns **top N₁ per branch**.

**N₁ depends on what comes after:**

| Downstream config | Recommended N₁ |
| --- | --- |
| No rerank, no expansion (early phase) | **30–50** |
| No rerank, with expansion | **30–50** |
| With rerank | **100–150** |

**Why the difference**: RRF fusion benefits from depth — items appearing in *both* lists get boosted, and you need headroom for overlap to emerge. But that only matters if a reranker is downstream to extract precision from the larger pool. Without a reranker, the final top-K is taken directly from the fused list, and going deeper than ~50 adds noise without benefit.

### 1b. Chunking strategy (structure-aware, target-size)

Used both for indexing and for splitting documents that don't fit a model's context. Same algorithm in both cases.

**Algorithm:**

1. **Short-circuit** — if the whole text fits `TargetChunkSize`, return it as a single chunk.
2. **Markdown-hierarchy split** — Markdig parses the document; cut at every `HeadingBlock`, producing one section per heading (plus any pre-heading preamble).
3. **Greedy pack** — sections are concatenated with `\n\n` and flushed when adding the next would exceed budget. Nuance: if the current buffer holds only a heading and the next section is oversized, the heading is carried forward as a prefix for that section's splits rather than emitted as a stub chunk.
4. **Oversized-section handling** — `SplitOversizedSection` peels off the heading, calls `RecursiveSplit` on the body, then re-prepends the heading to each piece so each chunk retains its semantic context.
5. **Recursive separator fallback** — tries separators in order: `\n\n` → `\n` → `". "` → `"; "` → `" "`. For each, greedy-merges parts via `MergeSplitsWithOverlap`, applying `ChunkOverlap` by carrying trailing parts of the previous chunk forward.
6. **Brute-force slice** — fixed-width slicing with overlap if no separator helps. Logs a warning.
7. **Tail absorption** — after packing, if the last chunk is smaller than `maxChars/4` and merging it back into the previous one still fits, they're combined.

**Implications for the rest of the pipeline:**

- **Chunk size is a target, not a guarantee.** Real chunks vary around 200 tokens — smaller for short sections, larger when no separator splits cleanly. Token math elsewhere in this doc (e.g. final payload size) is approximate.
- **Headings travel with their content.** This is a form of *built-in structural contextualization*, independent of the Anthropic-style LLM-generated prefix. The two stack; both contribute to disambiguation.
- **ChunkOverlap is split-time, not retrieval-time.** Different from post-retrieval expansion (Phase 4). Overlap helps embedding/match continuity at chunk boundaries; expansion helps generation by giving the LLM surrounding text. The two layer cleanly — don't conflate them.
- **±W expansion is structurally meaningful.** Neighboring chunks correspond to adjacent sections at the same hierarchical level (or splits of one oversized section), not arbitrary token windows. W = 1 reaches further "semantically" here than with a naive splitter, which may mean a smaller W is enough.

### 2. RRF fusion

- Reciprocal Rank Fusion combines the two ranked lists into one.
- Standard formula: `score(d) = Σ 1 / (k + rank_i(d))` with `k = 60` as the typical constant.
- Output: a single ranked list.

**Top N₂ kept after fusion depends on the next stage:**

| Next stage | N₂ |
| --- | --- |
| Direct to LLM (no rerank) | **8–12** (this is your final K) |
| Rerank | **30–80** (gives reranker enough candidates without being wasteful) |

### 3. Source-aware dedupe — **REMOVED**

Originally added because a single test query (the Brexit case) returned many chunks from doc A and pushed the relevant chunk of doc B out of the top 10.

**Why removed:**
- Reproduced from a single query — likely a query-formulation issue, not a structural one.
- Re-testing with a slightly rephrased query showed the right document at position 1 without dedupe.
- The rule "at most one chunk per source" hurts queries where the answer genuinely spans multiple chunks of one document.
- A reranker should handle this case properly by judging intent-relevance.

**To revisit later** (via eval harness):
- Cap per source at 2–3 (gentler than 1).
- MMR (Maximal Marginal Relevance) for principled relevance-vs-diversity tradeoff.
- Positional dedupe (collapse only adjacent chunks from the same source).

### 4. Reranking — *to be added*

- Reorders the N₂ candidates by intent-relevance using a cross-encoder (Cohere Rerank, Voyage Rerank, or similar).
- Operates on the **contextualized 200-token chunks** — *before* expansion. The contextualization prefix gives the reranker the disambiguation it needs; expansion is for the generator, not the reranker.
- Output: top **K = 8–12** survivors.

**Why before expansion (not after):**
- Rerankers are trained on short, focused passages.
- Expanding first dilutes the signal-to-noise per candidate.
- Reranker cost scales with input tokens; reranking 1000-token windows is ~5× the cost of 200-token chunks.

### 5. Chunk expansion ±W — *to be added*

- For each of the K survivors, fetch **W neighboring chunks before and after** from the original document.
- **Merge overlapping windows**: if two survivors are close enough in the source that their ±W windows overlap, merge them into one window. Saves tokens and avoids confusing the LLM with near-duplicates.
- Starting value: **W = 1** (conservative). Test W = 2 on the eval; may win for narrative content. With structure-aware chunking, W = 1 is more powerful than with naive splitters because neighbors are likely adjacent sections.

**Why this is orthogonal to contextualization and to top-K:**
- Contextualization helps the *retriever* find the right chunk.
- Expansion helps the *generator* understand it once found.
- Top-K controls how many distinct hits enter the context.

### 6. Send to LLM

Final payload size, rough math:

| Config | Final payload |
| --- | --- |
| K=10, W=0 (no expansion) | ~2k tokens |
| K=10, W=1 (±1 expansion) | ~5–6k tokens |
| K=10, W=2 (±2 expansion) | ~9–10k tokens |

Comfortably under any "lost in the middle" danger zone for current frontier models.

---

## Phased rollout

### Phase 0 — current state (before eval harness) *COMPLETED*

- Hybrid search → top **30–50** per branch
- RRF → top **8–12**
- Straight to LLM
- No rerank, no expansion, no dedupe

Goal: ship a working baseline. Don't tune anything yet — there's nothing to measure against.

**Carry-over tasks before Phase 1** (details in `2026-05-16-roadmap-phase-0/`):

- **Roll back source-aware dedupe** — currently active in `SearchPipeline`; remove per §3.
- **Expansion code stays dormant** — `ContextExpander` and `--expand-context` are already wired but off by default. Left in place; lit up in Phase 4.
- **Split options surface** — see `minerva-search-options.md`: separate `MinervaSearchOptions` from `MinervaIngestOptions` so search-only hosts (CLI, eval harness) don't pad chunking config.

### Phase 1 — build the eval harness *CURRENT*

- 20–30 hand-picked queries with known-good source IDs.
- Script that runs each query through the pipeline and checks whether the expected source is in top-K.
- Log per-stage outputs (which chunks survived semantic search, BM25, RRF, etc.) so regressions can be debugged at the right stage.
- **Baseline Phase 0** on this eval before changing anything.

Canonical phase-1 docs in `../roadmap-phase-1/`: `phase-1-spec.md` (decisions,
output contract, schema) and `phase-1-progress.md` (slice status, next
actions). 1A–1C are done; 1D (ship the public seed set + committed baseline +
notebook) is next. Completed investigations live in
`../roadmap-phase-1/completed/`; superseded drafts in
`../roadmap-phase-1/archive/`.

**Before 1D, two fixed points land first** — close the versioning gaps
(`--version` flag ✅ done; `vX.Y.Z` tags pending) and add `collection_metadata`
(designed in `../roadmap-phase-1/collection-metadata-design.md`) — so the
Wikipedia ingest creates documented collections rather than retrofitting
provenance later. Checklist in `../roadmap-phase-1/phase-1-progress.md`
("Before Phase 1D"). The corpus is ingested with `qwen2.5` (gemma4 shelved on
cost; eval measures relative deltas with the contextualizer held fixed).

### Phase 2 — add reranking

- Bump hybrid search to top **100–150** per branch.
- RRF → top **30–80**.
- Rerank → top **8–12** (final K).
- Measure vs Phase 0 baseline. Reranking is usually the single biggest single-step win in RAG.

### Phase 3 — review chunk size

The contextualization prefix is roughly 50–100 tokens. On 200-token target chunks, that's 25–33% of the embedded text *on average* (variable, given structure-aware chunking) — heavy weighting on document context, which is good for retrieval but may pull chunks that match the *doc* more than the *query content*. Headings already provide structural context inside each chunk, so the LLM-generated prefix may be doing redundant work.

**What to vary:**
- `TargetChunkSize`: try **300**, **400**, **512** tokens against the current 200.
- `ChunkOverlap`: may want to revisit jointly with target size.
- Contextualization prefix length: shorter prefix = less dilution at small chunk sizes. Especially worth testing given that headings already carry structural context.
- (Lower priority) the separator order in `RecursiveSplit` — current order is reasonable, unlikely to be the biggest lever.

**Why here and not earlier:**
- Requires full re-indexing (re-chunk → re-contextualize → re-embed → rebuild BM25). Expensive — you want a stable, reranked baseline to measure against.
- Decoupling this from expansion matters: ±1 expansion around 200-token chunks ≠ ±1 around 400-token chunks. Settle chunk size first, then tune W against it.

**Watch for:**
- Larger chunks → fewer total chunks → cheaper indexing and retrieval, but potentially worse precision.
- Larger chunks may make expansion *less necessary*, since each chunk already carries more context. The optimal (chunk_size, W) is a joint decision.
- Larger chunks also reduce how often `SplitOversizedSection` and the brute-force slice get triggered — your splits become more "natural" and less synthetic.
- Re-baseline Phase 2 results after re-indexing before drawing conclusions.

### Phase 4 — add expansion

**watch out**, see phase-1 docs: eval harness has not been prepared for this step and must be improved

- Use the chunk size chosen in Phase 3.
- Start at W = 1, merge overlapping windows.
- Measure vs Phase 3.
- Try W = 2 if W = 1 helps; pick the winner on the eval.
- If Phase 3 settled on larger chunks, W = 0 (no expansion) is also a valid candidate worth testing.

### Phase 5 — revisit deduplication

With reranking, chunk size, and expansion in place and the eval as ground truth:

- Test "no dedupe" vs "cap per source at 2–3" vs MMR.
- Pick whichever wins on the eval (and don't be surprised if "no dedupe" wins).

### Phase 6 — tune K

- Try K ∈ {8, 10, 12}. Diminishing returns; do this last.

---

## Parameter reference table

| Stage | Phase 0 (no rerank) | Phase 2+ (with rerank) |
| --- | --- | --- |
| Semantic top N₁ | 30–50 | 100–150 |
| BM25 top N₁ | 30–50 | 100–150 |
| RRF output N₂ | 8–12 (= final K) | 30–80 |
| Rerank output K | — | 8–12 |
| Expansion W | 0 | 0 → 1 → 2 (via eval) |
| Final LLM input | ~2k tokens | ~5–10k tokens |

---

## Cross-phase backlog

Items promoted from phase-1 detail docs because they are product-level, not
eval-harness decisions. They are not tied to a single phase.

- **Real BM25.** The lexical branch currently ranks with Postgres `ts_rank`,
  which is not true BM25 (no IDF or length normalization). Genuine BM25 needs
  a Postgres extension (ParadeDB / `pg_search`, or VectorChord-bm25). Out of
  scope now; revisit if the lexical branch underperforms once it is being
  measured. (From `roadmap-phase-1/completed/2026-06-07-fts-simple-fix.md`.)
- **Per-document language detection.** The corpus is mixed Italian/English.
  The FTS fix uses the `'simple'` config, which does not stem either language.
  Per-document language detection (selecting the right analyzer at ingest) is
  the correct long-term answer; deferred. (Same source.)
- **Ingestor versioning / reindex policy.** When ingestor code changes, what
  happens to existing collections — force reindex? in-place upgrade?
  signature enforcement? A Minerva-product policy question. The pre-1D
  `collection_metadata` work already settles the *config-drift* case (the
  reingest guard hard-fails on a changed invariant; see
  `../roadmap-phase-1/collection-metadata-design.md`). What remains here is the
  *code-drift* case — changes pinned only by the recorded ingestor SHA, not
  guarded. Decide after Phase 1 ships.
- **Model comparison & performance report** (research, after Phase 2) — see
  `docs/future/backlog.md`. Reconsider gemma4 / other local + online models;
  ingest per model; run the bench; publish a status report.

---

## Notes & caveats

- **200-token target + structure-aware chunking**: chunks vary in size, and each carries its heading. Headings already provide structural context; the LLM-generated contextualization prefix may be partly redundant. Phase 3 explicitly tests this.
- **Split-time overlap ≠ retrieval-time expansion.** `ChunkOverlap` (in chunking) and ±W (in retrieval) solve different problems and stack cleanly. Don't conflate them.
- **Log everything during eval runs**. Per-stage scores and survivors. Without this, debugging regressions is miserable.
- **Always run the full eval set**, not single queries. A change that helps query #7 may silently hurt query #23.
- **Don't stack changes blindly**. One change at a time, measure, then move on.
