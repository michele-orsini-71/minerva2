# Eval harness considerations

 > let's talk about the eval harness what is the general idea that arrives from this article? [contextual-retrieval article](https://www.anthropic.com/engineering/contextual-retrieval)
> - I have one or more collections (my notes and for example some wikipedia subsets, I have kiwix zim files, the 100 most popular articles, all wikipedia history ...)
> - I set pairs of query and a list of hits (maybe I also need the the perfect hit). What are hits? the article or the chunks, I suspect the original anthropic article mentions the chunk, this is what is returned by the retriever (all that dedup reasoning made me think so, do they use document and chunk interchangeably?)
> - run the queries and calculate context recall and context retrieval

## Chunk vs. document — what the Anthropic article actually does

You're right to suspect they use the terms loosely. Looking at how their eval is described:

- They use **passage-level / chunk-level retrieval evaluation**.
- The ground truth is: "for this query, _these specific chunks_ contain the answer."
- The metric they emphasize is **retrieval recall @ K** — did the gold chunks appear in the top-K returned?

So when they say "chunks" they really do mean chunks, and "documents" mostly appears in the framing/motivation, not the metric. The dedup discussion earlier in our conversation was about **practical retrieval behavior** (lots of similar chunks crowding out diverse sources), not about the eval ground-truth granularity.

But here's the catch: **chunk-level ground truth is annoying to build and brittle**. If you re-chunk the corpus (Phase 3!), all your gold chunk IDs become invalid. So most practical eval harnesses use a hybrid:

- **Document-level (or section-level) ground truth** for portability — "the answer is in this Wikipedia article" or "in this section of this doc".
- A **chunk → source mapping** is maintained at indexing time, so at eval time you check: did any chunk from a gold source appear in top-K?

This is robust across re-chunking and re-indexing. You only need chunk-level ground truth if you care about pinpointing the _specific_ paragraph, which matters for some specialized use cases but probably not yours.

## Recommended ground-truth schema

Something like:

json

```json
{
  "id": "q001",
  "query": "What did the 2024 ARERA decision change about Scambio sul Posto?",
  "collection": "personal-notes",
  "gold_sources": ["notes/energy/arera-2024-ssp.md"],
  "gold_sections": ["notes/energy/arera-2024-ssp.md#decision-2024-456"],  // optional, finer
  "answer_text": "...",  // optional, for answer-quality eval later
  "notes": "regression case from Brexit-style dedupe incident"
}
```

- `gold_sources` is the minimum viable ground truth. Easy to author, survives re-chunking.
- `gold_sections` is optional, useful if you want to measure finer-grained retrieval later.
- `answer_text` is for **end-to-end** eval (LLM-as-judge or similarity-based), which is a separate, harder, more expensive evaluation you can layer on later.

A query can have **multiple** gold sources — sometimes the answer genuinely requires synthesizing across documents.

## Metrics — what to actually compute

You mentioned "context recall and context retrieval". Slight terminology cleanup, since RAG eval has a confusing vocabulary:

**Retrieval-stage metrics** (what you want first — fast, cheap, deterministic):

- **Recall@K**: fraction of queries where _at least one_ gold source appears in top-K. The headline metric. This is what Anthropic reports.
- **MRR (Mean Reciprocal Rank)**: 1 / rank of the first gold hit, averaged over queries. Captures _how high_ the gold hit lands, not just whether it lands. Sensitive to ordering — useful when you're deciding between reranker variants.
- **Hit@1**: did the top-1 result come from a gold source? Strict version of Recall@K, useful for spot-checking.
- **nDCG@K**: only useful if you have _graded_ relevance (some sources more relevant than others) or _multiple_ gold sources per query and care about ranking. Probably overkill for now.

**Don't confuse these with**:

- **Context recall / context precision** (Ragas terminology) — these are LLM-judged metrics about whether the _generated answer_ draws from the retrieved context. Useful eventually, but they're end-to-end metrics, not retrieval metrics, and they require an LLM judge per query (slow + costs money + introduces judge noise).

**My suggestion**: start with **Recall@K** (for K = 5, 10, 20) and **MRR@10**. Two numbers, deterministic, fast, no LLM in the loop. They'll tell you 90% of what you need for Phases 2–6.

## The harness itself — minimum viable shape

```text
eval/
  datasets/
    personal-notes-v1.jsonl       # one query per line
    wikipedia-top100-v1.jsonl
  runners/
    run_eval.py                   # query → pipeline → top-K → compare to gold
    pipelines/
      phase0.py                   # current baseline
      phase2_rerank.py
      ...
  results/
    2026-05-18_phase0_personal-notes.json
    ...
  notebooks/
    compare_runs.ipynb            # diff two result files, find regressions per query
```

**Key behaviors:**

- Pipeline config is **explicit and serialized into the result file** — chunk size, K, reranker model, embedding model, prompt versions. Future-you needs to know exactly what produced each number.
- **Per-query results** are logged, not just aggregates. When Recall@10 drops 5%, you need to know _which queries broke_ — that's where the signal is.
- **Per-stage logging** within a query — after semantic search, after BM25, after RRF, after rerank — so a regression can be localized to the right stage.
- **Deterministic** where possible. Fix embedding model version, fix reranker version, fix the random seed if anything stochastic is involved. Otherwise reruns of "the same" pipeline differ, and you can't tell change from noise.

## Building the datasets — practical tips

This is the part everyone underestimates:

- **20–30 queries is a fine starting point**. You'll want 50–100 eventually, but don't block on it.
- **Include the queries that already taught you something** — the Brexit case, anything that produced surprising behavior. Those are _gold_ for regression testing.
- **Mix easy and hard**. Some queries where you know retrieval should be trivial (sanity check), some where you know it's hard (where improvements will show).
- **Authoring trick**: take a document you know well, write a query whose answer you know lives in it. That's one row. Repeat. Much faster than trying to think up queries from scratch.
- **LLM-assisted authoring**: Claude can read a document and generate plausible queries + gold spans. Useful for bulk, but **review every one** — LLM-generated queries are often too on-the-nose (vocabulary mirrors the doc, so retrieval is artificially easy).
- **Wikipedia subsets are great for this** because the corpus is public, you can share/discuss queries without leaking personal info, and answers are checkable.

## Two collections, two ground-truth files

You mentioned multiple collections — personal notes, Wikipedia subsets. These should be **separate eval datasets**, run independently. Reasons:

- Different collections may need different chunk sizes, different K, etc. Don't average across them.
- Personal notes and Wikipedia have very different characteristics — Wikipedia is well-structured, your notes are more idiosyncratic. Conflating them hides where problems live.
- Running them separately lets you see if a change that helps Wikipedia hurts your notes (very possible).

## What to defer

Don't build these in v1:

- **Answer-quality / LLM-as-judge metrics**. Add later when retrieval is solid. They're noisy, expensive, and won't help you decide between reranker variants.
- **Fancy UIs / dashboards**. A CSV / JSON output and a notebook for comparing runs is enough for months.
- **Auto-generated query banks at scale**. Start hand-curated; you'll have better insight into why things fail.
- **Statistical significance testing**. With 30 queries the variance is high; just look at the per-query diffs.

## To recap your mental model

- Ground truth: **`(query, gold_source_ids)`** pairs. Source-level, not chunk-level — survives re-chunking.
- Metric: **Recall@K** plus **MRR@10**, computed by checking if retrieved chunks come from gold sources.
- Output: per-query results logged with full pipeline config, plus aggregate numbers.
- Anthropic's article does measure at chunk granularity, but for your purposes source-level ground truth is more practical and lets you iterate on chunking itself.

When you actually start building, the order I'd suggest: write 5 queries by hand → write the dumbest possible runner that computes Recall@10 for those 5 → confirm it produces sane numbers on Phase 0 → _then_ expand the dataset and add metrics. You'll find a dozen small issues (path handling, source ID normalization, what counts as a "hit") that are much cheaper to fix at 5 queries than at 50.

## How will you understand improvements/worsenings?

The honest answer is: you don't get **statistical certainty** at this sample size — you get **informed engineering judgment**, supported by data. Here's the workflow:

**1. Look at aggregates first, but only for direction.**

After each phase, compute Recall@5/10/20 and MRR@10 across the whole eval set against the previous phase's numbers. Did they go up, down, or sideways? With 30 queries, the magnitude of a 1–2pp change is in the noise floor; a 5+ pp change is meaningful even at this N. The roadmap actually predicts the shape of each phase's signal:

- Phase 2 (rerank) — "usually the single biggest single-step win", expect Recall@10 to move noticeably (often +5–10pp). If you don't see it, something is wrong, not noise.
- Phase 3 (chunk size) — likely smaller effects, may be mixed with re-indexing variance.
- Phase 6 (K tuning) — "diminishing returns", small effects; this phase is exactly where you can't trust aggregates and the per-query view becomes everything.

**2. The per-query diff is the real decision tool.**

The aggregate tells you _whether_ things moved. The per-query diff tells you _whether you like how they moved_. A made-up example for a Phase 2 reranker comparison:

```text
Improved (rank went up or newly hit):  21 queries
Regressed (rank went down or lost):     3 queries
Unchanged:                              6 queries
```

Now you read the 3 regressions. Maybe they're all "near-miss" cases where the gold doc fell from rank 4 to rank 6 — still in top-10, still findable, just not as highly ranked. Annoying but not fatal. Or maybe one of them is a query you'd previously _specifically added_ as a regression test (the Brexit case the prior doc mentions). That carries more weight than its 1/30 ratio suggests.

This is the move: weigh the wins vs. losses by **what kinds of queries they are**, not by counting. 21 generic wins + 1 critical loss might mean "don't ship". 5 modest wins + 0 losses might mean "ship".

**3. Treat the eval as a regression net, not a scoreboard.**

The eval set codifies queries you know should work. A change that improves aggregates but breaks a known-important query is a regression even if the average looks good. Conversely, a change that doesn't move aggregates but recovers a previously-broken query is progress. The point of hand-curating the eval set is that _every query is in there for a reason_ — when a query state changes, that's a story worth reading, not a number to average over.

**4. Calibrate your noise floor over time.**

Run the same pipeline twice against the same collection. If aggregate Recall@10 differs by 0.5pp between runs (HNSW build noise, etc.), then 0.5pp is your noise floor — anything smaller is meaningless. After a few phases you'll have intuition for what magnitudes of aggregate change are real on _your_ eval set.

**5. Comparisons are phase-to-phase, not always-vs-Phase-0.**

Each phase is normally compared against the _previous_ phase. Phase 2 vs Phase 0. Phase 3 vs Phase 2. Phase 4 vs Phase 3. This lets you see whether each _step_ added value. Occasionally you'll also want a Phase N vs Phase 0 comparison to see cumulative effect, and rarely a "what if I skip phase N" check — but the default is incremental.

**TL;DR**: aggregates tell you direction; per-query diffs tell you whether you like the trade; known-important queries get extra weight; effects smaller than your re-run noise floor are noise. With 30 queries this is judgment, not statistics. With 300+ queries you could add a paired t-test for the aggregate confidence layer, but you'd still make the actual ship/don't-ship decision from the per-query view.
