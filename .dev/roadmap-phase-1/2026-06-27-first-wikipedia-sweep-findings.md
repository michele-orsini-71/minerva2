---
slug: first-wikipedia-sweep-findings
title: Phase 1D — First Wikipedia Sweep — Findings & Analysis Notebook Spec
status: active
parent: phase-1-progress.md
date: 2026-06-27
---

# First Wikipedia sweep — findings and notebook spec

The first real Phase 1D run: the bench (`Minerva.Search.Bench`) over the
400-article distractor-grown Wikipedia corpus, two contextualization settings,
`wikipedia-v1` dataset (32 queries, 26 distinct gold articles). This is the
first time the public eval runs against a corpus with real distractor density,
so it is also the first time the metrics can decide anything.

## The run

Two sweeps, each `top_k=50`, `hybrid_alpha ∈ {0.3, 0.5, 0.7}`, `n=32`.

**`wikipedia-nollm`** (no contextualization):

| α | R@5 | R@10 | R@20 | MRR@10 |
| --- | --- | --- | --- | --- |
| 0.3 | 0.672 | 0.750 | 0.812 | 0.650 |
| 0.5 | 0.875 | 0.953 | 0.953 | 0.788 |
| 0.7 | 0.922 | 0.922 | 0.984 | 0.828 |

**`wikipedia-qwen2-5`** (qwen2.5 contextualization):

| α | R@5 | R@10 | R@20 | MRR@10 |
| --- | --- | --- | --- | --- |
| 0.3 | 0.672 | 0.688 | 0.797 | 0.651 |
| 0.5 | 0.880 | 0.922 | 0.953 | 0.828 |
| 0.7 | 0.896 | 0.896 | 0.969 | 0.836 |

## The ruler: n=32, so 1 query = 0.031

Every metric moves in steps of `1/32 ≈ 0.031`. Most gaps between the two
contextualization settings are 1–2 queries wide. Do not read them as effects
until the failure audit confirms the failing queries are real failures and not
mislabels.

## Findings

1. **α=0.3 is too low; 0.5 and 0.7 are roughly tied.** Recall rises with α, but
   not strictly monotonic — nollm R@10 dips from 0.953 (α=0.5) to 0.922 (α=0.7).
   The trend is real, the wobble is noise.

2. **Contextualization did not improve recall, and may slightly hurt it — but
   the difference is inside the noise floor.** The nollm recall advantage is 0–2
   queries in every cell (e.g. α=0.7 R@10: 0.922 vs 0.896 = exactly one query).
   Cannot be distinguished from zero at n=32. **Do not yet claim "nollm wins on
   recall."**

3. **Recall climbs with K (R@20 > R@10 > R@5)** — true by construction. The size
   of the gap is the signal: where it is large, ranking (not retrieval) is the
   bottleneck for those queries.

4. **The real headline — contextualization buys ranking, not coverage.** This is
   the sharpest pattern and it is *directionally consistent across all three α*,
   unlike the recall gaps:
   - qwen MRR@10 ≥ nollm MRR@10 at every α (0.828 vs 0.788 at α=0.5).
   - qwen recall ≤ nollm recall at every α.

   Coherent story, not a contradiction: contextualization adds discriminative
   signal that **reorders** a found gold doc higher (better MRR) but does not
   help **find** golds that were missed (no recall gain). Worth keeping even at
   n=32 because the direction holds across every cell.

**Net:** the comparison is one bad label away from flipping. Which is exactly
why the label audit below gates any conclusion about contextualization.

## Open methodological doubt — hand-written labels

The eval gold was authored by hand, from memory, against 400 articles that
cannot all be held in mind. Two known IR problems apply:

- **Incomplete relevance judgments (pool bias).** A genuinely relevant article
  that was never labelled gold is scored as a miss.
- **Single-annotator subjectivity.** Gold is "what *I* think is the best match."

**Key insight — the damage is asymmetric.** It poisons the *zeros*, not the
*ones*:

- Success/Recall = 1 (gold found) → trustworthy.
- Success/Recall = 0 (gold missed) → **suspect**: either the retriever failed,
  or it returned a relevant-but-unlabelled article scored as a miss. The number
  alone cannot tell these apart.

So spend audit effort on the failing queries only.

**Standard fix — pooling.** Invert the labelling: run retrieval first, then
judge the top-K *actually retrieved* articles, and promote any genuinely
relevant ones into `gold_sources`. This is the TREC method. It converts "did I
remember the best article?" into "is this concrete candidate relevant?" — easier
and far more reliable. Limitation: pooling only finds relevant docs some
configuration surfaces; an article no config ever ranks in top-K stays invisible
(small residual with a decent retriever over 400 docs).

**Bias half — optional mitigations.** An LLM-as-judge as a second annotator
(disagreements between you and the judge mark the labels worth re-examining).
For `mynotes` the bias is not a bug — it is a personal RAG, the user *is* the
ground truth. For `wikipedia` (public, more objective), pooling + LLM judge pays
off most.

**Procedure decided:** the first run becomes a labelling tool, not just a
measurement.
1. Run the sweep as-is (done).
2. Pull every failing query's top-K retrieved articles into a review pass.
3. Promote genuinely-relevant ones to gold, then re-score. The number after
   step 3 is the trustworthy one.

## Corpus and output facts (for the notebook)

- **Corpus:** 400 flat `.md` files at `minerva2-index/wikipedia-en-corpus/`,
  named exactly by `source_id` (`Arthropod.md`). Each has YAML frontmatter
  (`title:` …) then a `# Heading` then body.
- **Results:** `eval/results/<timestamp>_wikipedia-v1/` per run —
  `run.json` (has `collection`, the only reliable way to tell nollm from qwen
  apart, since both share the `wikipedia-v1` dataset name and differ only by
  timestamp), `metrics.csv` (dot-decimals; the Italian commas were console-only),
  `details.jsonl` (query, gold_sources, metrics, `hits[]` with rank, source_id,
  score, gold_hit — **no chunk text**, so snippets come from reading the corpus
  `.md`).

## Notebook spec — `eval/notebooks/phase0-analysis.ipynb`

This is the Phase 1D notebook deliverable. It does double duty: the committed
baseline analysis *and* the failure audit above. Replaces the manual Numbers
spreadsheet.

**Cells**

1. **Config** — `RESULTS_DIR`, `CORPUS_DIR` constants at top, nothing hard-coded
   deeper.
2. **Loader** — glob result folders; per folder read `run.json` for `collection`,
   load `metrics.csv`, tag rows with `collection`; concatenate all runs.
3. **Metrics view** (replaces Numbers) — group by `(collection, hybrid_alpha)`,
   mean over queries → `R@5/10/20, MRR@10, n`, styled as a heatmap. Note in-cell:
   *1 query = 0.031*.
4. **Failure picker** — params `collection`, `hybrid_alpha`, metric (e.g.
   `recall_at_10`); filter `details.jsonl` rows where it `== 0`.
5. **Audit view** — per failing query print: query · gold_sources · top-10
   retrieved (rank, score, gold_hit, title, snippet). The cell that replaces
   spreadsheet scrolling.
6. *(later, optional)* **Label-fix emit** — collect "actually relevant" marks,
   write an updated `wikipedia-v1.jsonl`. Build only when the audit shows it is
   needed.

**Gotchas**

- Two runs share the dataset name → disambiguate by `run.json["collection"]`,
  never by folder name.
- `metrics.csv` decimals are `.` — pandas reads them directly.
- Hits are chunks, gold is by article → **dedupe hits by `source_id` keeping the
  best (lowest) rank** before showing top-10, or one article eats several slots.
- Snippet: strip frontmatter (between first two `---`), take first paragraph
  after `# Heading`, ~300 chars.
- Keep `CORPUS_DIR + source_id` so the same notebook works for mynotes later
  (mynotes source_ids carry subpaths; wikipedia are flat filenames).

**Verification**

1. Metrics table reproduces the six aggregate lines above exactly → loader
   correct.
2. `(wikipedia-nollm, α=0.5, recall_at_10)` failure picker returns the
   zero-recall queries, each rendered with gold + top-10 titles → audit usable,
   and the methodology doubt gets resolved query by query.

## Status

- [x] First Wikipedia sweep run (nollm + qwen2-5).
- [ ] Build `phase0-analysis.ipynb` (authored by the user as manual-coding work;
      spec above).
- [ ] Failure audit → promote mislabelled golds → re-score.
- [ ] Commit the trustworthy baseline (Phase 1D deliverable 3) only after the
      audit.
