# Why contextualization is slow (and cheap to store)

Root-cause analysis (2026-05-07) of the long pauses during ingestion when
contextual preprocessing is enabled. Ingesting ~3k notes / ~9k chunks took
**20+ hours with contextualization on, ~10 minutes off**.

## The three operations, not to be confused

| Step | Calls per doc | Engine | Cost |
| --- | --- | --- | --- |
| 1. Summarize | 1 (or 1 per segment) | LLM (e.g. Gemma) | one-off, cheap |
| 2. **Contextualize** | **N — one per chunk** | LLM | **the bottleneck** |
| 3. Embed | 1 batch | dedicated embedding model | fast |

Embedding (step 3) is fast because embedding models are small encoders, not
generative LLMs — a different cost class. The slowness is entirely step 2.

## Why step 2 is slow — two compounding causes

The summary is generated **once** per document and reused as a string; it is
**not** regenerated per chunk, and the database stores only the LLM's per-chunk
output, not the summary (so there are no duplicate summaries in the DB). The
cost is structural:

1. **Prefill paid on every call.** Each chunk's prompt embeds the same summary
   as input: `<document>{summary}</document><chunk>{this chunk}</chunk>`. From
   the model server's view each chunk is an independent HTTP request, so unless
   a KV/prefix cache reuses the identical prefix, the model re-reads (prefills)
   the whole summary every time, *before* generating its one-sentence output.
   Prefill on a multi-KB summary is what looks like a pause; token generation
   (what you see testing the model directly) is fast.
2. **The loop is strictly serial.** Contextualization is a `for` with `await`
   per chunk, no concurrency. 9k chunks × ~8 s/call ≈ 20 h — matching the
   observed number.

Net cost: `O(chunks × summary_prefill)`, paid serially.

## Levers (no quality loss)

- **Prefix / KV caching** on the model server — turns N prefills into 1 per
  segment. Probably the single biggest lever.
- **Shorter summaries** — prefill is roughly linear in summary length × chunk
  count; halving the summary halves total prefill.
- **Concurrency** — a small in-flight degree (2–4) gated by the existing rate
  limiter cuts wall time even without prefix caching.
- **Per-chunk gating** — skip contextualization for tiny chunks where the
  prefix barely helps.

See [model-speedups.md](model-speedups.md) for the model-server-side levers.

## Storage cost is negligible

The expensive part of contextualization is *time*, not *space*. End to end,
making the corpus semantically searchable costs ~**20× the source-text size**,
and **~95% of that is the dense vectors themselves**, unrelated to
contextualization. The contextual prefix adds only ~1.5% on top (≈2 MB on a
140 MB store). See [storage-footprint.md](storage-footprint.md) for the
per-collection breakdown and the SQL to measure it.
