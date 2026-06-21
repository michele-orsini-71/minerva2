# Speeding up chunk-context generation (LM Studio + Gemma)

Levers for the per-chunk contextualization LLM call, the dominant ingestion
cost (see [contextualization-cost.md](contextualization-cost.md) and
[ingestion-timing.md](ingestion-timing.md)). `gemma-4-e4b` is served via
LM Studio (`http://localhost:1234/v1`); generation is autoregressive, so
multi-token / speculative-decoding techniques apply.

## Multi-token prediction in brief

Instead of one token per forward pass, the model emits several:

- **MTP heads** (DeepSeek-V3 style) — extra output heads predict positions
  t+1, t+2, t+3 in one pass; enables self-speculative decoding.
- **Speculative decoding** — a small *draft* model proposes N tokens, the big
  *target* model verifies them in one parallel forward pass. Lossless: wrong
  drafts are rejected.
- **Medusa / Eagle** — bolt-on heads on an existing model, no retrain.

You do not implement these — you enable them in the inference engine
(LM Studio's *Speculative Decoding* toggle, llama.cpp `--draft`, MLX
`--draft-model`, vLLM, TGI, …). Typical speedup **1.5×–3× tokens/sec, no
quality loss**. Caveat: this helps only *generative* decoding; it does nothing
for embedding models (single forward pass, no autoregression).

## Ordered levers for our setup

By effort versus payoff:

1. **Concurrency > 1** (`Chunking.Llm.Concurrency`) — LM Studio handles
   parallel requests; serial is the most obvious bottleneck. Try 4–8, watch
   RAM. Free win. *Measured caveat: on a small model the GPU saturates at
   concurrency 1, so the real gain was modest — see [Measured results](#measured-results).*
2. **Speculative decoding in LM Studio** — pick a small draft from the same
   family (Gemma-4 270M or 1B as draft for the e4b target). 1.5×–2.5× on Apple
   Silicon, no quality change. This is the multi-token-prediction lever,
   packaged. Free win.
3. **Choose the backend per model size (GGUF vs MLX)** — MLX was reported
   meaningfully faster for Gemma on Apple Silicon, and LM Studio exposes MLX
   builds directly. *But this is not universal:* on `qwen2.5-0.5b` we measured
   GGUF about 1.65× faster than MLX (see [Measured results](#measured-results)).
   A plausible reading is that GGUF wins on small models and MLX on larger ones,
   but this is unmeasured — treat the backend as a per-model choice to benchmark,
   not a fixed answer. Note: LM Studio's parallel-request support requires the
   GGUF/llama.cpp engine (MLX parallelism was not yet available).
4. **Right-size the model** — chunk-context generation is narrow and
   low-creativity; Gemma-4 1B or Qwen2.5-1.5B-Instruct may produce equally
   usable blurbs far cheaper. Needs an eval-harness A/B before committing.
5. **Audit prompt layout** — prefix caching only helps when the document text
   is byte-identical and first in the prompt; verify the prompt maximizes
   prefix-cache hits.

Recommended order **(1) → (2) → (3) → (4)**. The first two are free; the last
two need the eval harness to confirm context quality does not regress.

## Measured results

Measured 2026-06-21 on **Apple M2 Pro, 32 GB**, contextualization model
**`qwen2.5-0.5b`** served via LM Studio. Workload: the same 6 Wikipedia
articles (1186 chunks) ingested per run, so totals are directly comparable.
These numbers are specific to this machine and model; treat them as direction,
not absolutes.

### Engine: GGUF vs MLX (sequential)

| Engine | Total | Throughput |
| --- | --- | --- |
| MLX | 33m55s | 0.58 chunks/s |
| GGUF | 20m37s | 0.96 chunks/s |

GGUF was about **1.65× faster** than MLX for this small model — the largest
single win of the session, and it contradicts the general "MLX is faster on
Apple Silicon" guidance (which was reported for the larger Gemma model). See
lever (3) above.

### Concurrency, after fixing a latent serialization bug

`ChunkContextualizer.ContextualizeAsync` issued contextualization calls in a
sequential `for`-loop that awaited each call before starting the next, so
`Chunking.Llm.Concurrency` had **no effect** on contextualization regardless of
its value. The fix issues the per-chunk calls together and lets the provider's
`RateLimiter` semaphore bound the actual concurrency (`Task.WhenAll`, which
preserves the prefix-to-chunk order). Only after this fix does the knob do
anything.

Post-fix sweep (GGUF):

| Concurrency | Total | vs sequential |
| --- | --- | --- |
| 1 | 20m37s | baseline |
| 2 | 19m54s | −3.5% |
| 4 | 18m13s | −11.6% |

The gain is modest because the M2 GPU already sits at roughly **90–100%**
utilization (observed with `asitop`) at concurrency 1, so a single request
nearly saturates compute and extra parallelism only fills the small remaining
headroom. Per-chunk steady-state was about 14% better at concurrency 2 once the
warm-up document is excluded.

**Caveat:** the machine was under memory pressure during the runs (RAM
26.4/32 GB, swap 18.2/19 GB nearly full), which adds paging noise to the
per-document times. `throttle: no` at the moment of capture, so the limit was
GPU compute, not thermal.

### Takeaway

For a small contextualization model on this hardware, the **engine choice
(GGUF) was the real lever**; concurrency is a minor improvement once the GPU
saturates. The prompt-prefix lever (5) is also limited here, because the
contextualization prompt carries a short document *summary* rather than the full
article, so the shared cacheable prefix is small.
