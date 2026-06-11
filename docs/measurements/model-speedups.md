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
   RAM. Free win.
2. **Speculative decoding in LM Studio** — pick a small draft from the same
   family (Gemma-4 270M or 1B as draft for the e4b target). 1.5×–2.5× on Apple
   Silicon, no quality change. This is the multi-token-prediction lever,
   packaged. Free win.
3. **Switch backend to MLX** (if currently GGUF) — MLX is meaningfully faster
   for Gemma on Apple Silicon; LM Studio exposes MLX builds directly.
4. **Right-size the model** — chunk-context generation is narrow and
   low-creativity; Gemma-4 1B or Qwen2.5-1.5B-Instruct may produce equally
   usable blurbs far cheaper. Needs an eval-harness A/B before committing.
5. **Audit prompt layout** — prefix caching only helps when the document text
   is byte-identical and first in the prompt; verify the prompt maximizes
   prefix-cache hits.

Recommended order **(1) → (2) → (3) → (4)**. The first two are free; the last
two need the eval harness to confirm context quality does not regress.
