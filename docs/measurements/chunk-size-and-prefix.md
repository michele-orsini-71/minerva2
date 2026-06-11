# Chunk size, overlap, and the contextual prefix

Reasoning (2026-05-05) about whether chunk size and overlap should change now
that a contextual prefix is prepended before embedding. Conclusion: leave the
current `(1200 chars, 200 overlap)` alone unless the eval harness says
otherwise; the high prefix-to-chunk ratio is a small-chunk artefact, not a
problem.

## The prefix is on-spec; the ratio noise comes from small chunks

The contextualizer asks for a short succinct context and gets back ~150–300
chars (~40–80 tokens) — the same band as the Anthropic reference examples. The
alarming "prefix almost as long as the chunk" cases are all **tiny chunks**
(a heading plus a line or two), where any one descriptive sentence dominates:

| chunk | prefix chars | chunk chars | ratio |
|---|---|---|---|
| heading-only chunk | 227 | 77 | 2.95× |
| short body | 121 | 163 | 0.74× |
| full procedure | 270 | 1226 | 0.22× |
| full body | 201 | 1190 | 0.17× |

For real bodies of text (>800 chars) the prefix is 15–30% — exactly the
intent. The prefix feeds **only the dense embedding**, not the tsvector, so
even when prefix > chunk it does not pollute the lexical leg; it just means a
tiny chunk's embedding is dominated by its context sentence, which is the point
of contextual retrieval for short fragments. The real lever for the visual
noise is a **chunk minimum size** (merge tiny leading/trailing chunks), not the
contextualizer prompt.

## Should the prefix make us raise chunk size / overlap?

Probably not for quality; possibly for cost. The two intuitions oppose:

- **Smaller is fine now.** A reason for large chunks was "embed coherently in
  isolation" — the prefix already supplies that, so quality pressure to stay
  large drops. Same for **overlap**: it exists so a phrase split across chunks
  survives intact somewhere; with both halves embedding under the same document
  context, a split is far less catastrophic. Overlap could plausibly drop to
  50–100, or zero.
- **Larger is cheaper.** A 200-char prefix on a 1200-char chunk is ~17% LLM
  overhead per indexed character. Doubling chunk size halves the LLM cost per
  character and halves the embedding count.

The Anthropic article measured its +35% holding chunk size constant — there is
no "the article says go bigger" argument. This is a cost/quality trade-off
specific to our setup.

## What "good" means — failure modes

| Symptom | Cause | Fix |
|---|---|---|
| Right document not in top-K though chunks contain the query terms | chunks too **small** — vector dominated by short surrounding text | raise size |
| Top-K full of chunks that merely "mention" the query | chunks too **large** — topic dilution | lower size |
| Right document found but a weaker chunk returned | boundary issue, not size | tweak the splitter |

Embedding-model ceiling: most embedding models degrade past ~512 tokens of
meaningful content. 1200 chars ≈ 300 tokens (safe); 1800–2400 chars
(~500–650 tokens) is the dilution-risk band. Soft upper bound: **do not exceed
~2000 chars** without checking the embedder's model card.

## The test — a small offline retrieval eval

The only way to actually decide. Build ~30–50 `(query, expected_source_id)`
pairs (document-level target, robust to re-chunking), ingest the corpus into N
collections varying chunk config — e.g. `(800,100)`, `(1200,200)`,
`(1800,150)`, `(2400,100)`, plus `(1200,0)` to isolate overlap — with
contextualization on for all, then score **Recall@5/10** and **MRR**. Pick the
best, breaking ties by chunk count (fewer = cheaper). If the curve is flat,
`(1200,200)` is fine.

Cheap sanity checks first: if documents average 1–2 chunks each, chunk size
barely affects retrieval (nowhere to split badly) — prioritize the eval only
with many ≥4-chunk documents.

> This methodology is exactly what the Phase 1 eval harness now provides; the
> chunk-size sweep is **roadmap Phase 3**. The harness uses document-level
> ground truth and the same Recall@K + MRR metrics described here. See
> `.dev/roadmap-phase-1/phase-1-spec.md`.
