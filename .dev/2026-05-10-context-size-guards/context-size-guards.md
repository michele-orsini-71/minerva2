# Context-Size Guards — Design Notes

Working notes captured before pausing this thread to switch tasks. The goal is to be able to resume implementation later with full context.

## 1. The Problem

While running `run-obsidian-indexing-test-3-qwen2-5-test.sh` against an Obsidian vault, ingestion crashed with:

```text
Unhandled exception: Minerva.Exceptions.ProviderUnavailableException:
LLM API request failed (HTTP 400): Service request failed.
```

Stack trace pointed at:

- [OpenAICompatibleLlmProvider.cs:206](../../src/Minerva/Providers/OpenAICompatibleLlmProvider.cs#L206) → [DocumentSummarizer.cs:19](../../src/Minerva/Ingestion/DocumentSummarizer.cs#L19) → [IngestionPipeline.cs:156](../../src/Minerva/Ingestion/IngestionPipeline.cs#L156) (the `ProcessSingleDocumentAsync` branch).

The 400 was rejected by the LLM endpoint — not a transient error (429/5xx would have been retried by the resilience pipeline at [OpenAICompatibleLlmProvider.cs:264](../../src/Minerva/Providers/OpenAICompatibleLlmProvider.cs#L264)).

### Context

- **LLM**: `qwen2.5-0.5b-instruct-mlx` served by LM Studio at `http://localhost:1234/v1` (see `appsettings.qwen2-5-test.json`).
- **Why such a small model**: deliberately picked for speed. Larger models (e.g. `gemma-3` family) took ~2 days to ingest 9000 chunks. The 0.5B/MLX combo is for fast iteration.
- **Nominal context**: Qwen2.5 family is documented at 32k tokens, but the **loaded** context in LM Studio MLX builds is often smaller (default 4096/8192 unless explicitly configured).
- **Suspect inputs**: the largest files in `1-projects/` are `it/2025-09 MacTree.md` (~178k chars) and `it/Minerva RAG System/Minerva RAG Log.md` (~125k chars). The crash happened in the `it/` folder right after `2026-04 How to Code with AIs.md` succeeded, so one of these is the likely trigger.
- **Where the input gets sent whole**: `DocumentSummarizer.SummarizeAsync` ([DocumentSummarizer.cs:17-21](../../src/Minerva/Ingestion/DocumentSummarizer.cs#L17-L21)) forwards the full document text to the LLM, no truncation.

### Why the current pipeline lets this through

`IngestionPipeline.ProcessDocumentAsync` ([IngestionPipeline.cs:133-144](../../src/Minerva/Ingestion/IngestionPipeline.cs#L133-L144)) routes by segment count:

- `segments == 1` → `ProcessSingleDocumentAsync` → summarizes the **whole document** in one call.
- `segments > 1` → `ProcessLargeDocumentAsync` → summarizes **each segment** separately.

The crash trace says we hit the Single path. So we had `segments == 1` but `chunks > 1` — i.e. the segmenter returned the whole document as a single segment, and we sent ~50–180k chars to a model loaded with maybe 4–8k tokens of context.

Looking at [DocumentChunker.cs](../../src/Minerva/Ingestion/DocumentChunker.cs):

- `SegmentDocument` ([line 40-46](../../src/Minerva/Ingestion/DocumentChunker.cs#L40-L46)): if `text.Length <= LargeDocumentThreshold` (default 8000) → returns `[text]` whole.
- `SplitIntoSegments` ([line 286-349](../../src/Minerva/Ingestion/DocumentChunker.cs#L286-L349)): otherwise, splits by highest-level heading and **greedily merges** sections so each merged segment stays under threshold. But:
  - **No upper bound on a single segment.** If one heading-bound section is itself larger than the threshold, it's emitted as-is.
  - **Documents with no headings at all** (line 298-299) → returned as one segment regardless of size.

So the segmenter's threshold is a *floor for splitting*, not a *ceiling on segment size*. That's the design gap that caused the crash.

By contrast, the chunker (`SplitIntoChunks` → `SplitOversizedSection` → `RecursiveSplit`, [line 212-246](../../src/Minerva/Ingestion/DocumentChunker.cs#L212-L246)) does enforce a ceiling — except in the "no separator found" branch ([line 239-245](../../src/Minerva/Ingestion/DocumentChunker.cs#L239-L245)) where it logs a warning and emits the oversized chunk anyway.

## 2. Discussion — Solutions Considered

### Option A — Lower `LargeDocumentThreshold`

**Pros**: pure config change, no code.

**Cons**: doesn't actually fix the bug. The threshold is an input to `SegmentDocument`, but the segmenter only guarantees "if doc > threshold, *try* to split"; it doesn't guarantee `segment_size ≤ threshold`. Lowering it might catch some cases but leaves the underlying gap (headerless docs, single oversized headings).

**Verdict**: rejected as primary fix. Useful as a tuning knob alongside the real fix.

### Option B — Server-side: configure LM Studio with a larger context window

**Pros**: zero code change, fastest to verify.

**Cons**: a 0.5B model summarizing tens of thousands of tokens produces poor summaries anyway. Buys robustness against today's failure but not against the next big doc; doesn't solve the design gap. And every operator has to remember to do it.

**Verdict**: legitimate stop-gap, not a real fix.

### Option C — Truncate inside `DocumentSummarizer`

**Pros**: simple, defensive, one place to change.

**Cons**: the summarizer silently drops content. The chunker stays unaware of model limits. A code smell — wrong layer for size enforcement.

**Verdict**: useful as a *defense in depth* belt at the boundary, not as the architectural fix.

### Option D — Switch to token-based settings throughout

This was a longer discussion. The question: LLMs reason in tokens; why is all our logic in characters?

**Why characters today (deliberate choice, not oversight)**:

1. Tokenization is model-specific (Qwen, Gemma, Llama, BGE-M3, OpenAI all differ).
2. Chars are zero-dependency, `O(1)`, synchronous.
3. Chunking is dominated by *semantic boundaries* (headings, paragraphs); size is a guardrail, not the boundary-finding logic.
4. Keeps `Minerva.Ingestion` decoupled from `Minerva.Providers`.

**What full migration to tokens would require**:

- An `ITokenCounter` abstraction; ideally encode/decode for token-space chunking.
- **Two tokenizers**, not one: chunker bounds chunk size for the *embedder* (BGE-M3 = 8192 tokens); summarizer bounds for the *LLM* (different model, different tokenizer).
- Sourcing tokenizers: in-process (Microsoft.ML.Tokenizers — supports Llama, Tiktoken, SentencePiece; requires shipping vocab files), server-side `/tokenize` endpoint (network roundtrip per measurement, not portable), or estimation (chars × ratio).
- Algorithm rewrite: today's `SplitIntoChunks` and `MergeSplitsWithOverlap` measure `.Length` constantly; naive `Count(...)` swap turns hot loops `O(n²)`. Real fix tokenizes once up front and works in token space — different algorithm.
- Config schema break: `TargetChunkSize`, `ChunkOverlap`, `LargeDocumentThreshold` change meaning.
- Re-index implications: tokenizer-dependent chunk boundaries → switching embedders changes content hashes → forced re-ingest.
- Tokenizer-not-available fallback (unknown model) → keep chars-with-ratio anyway.

**Verdict**: a real project (weeks). Worth doing eventually if precision becomes an issue. **Not** what we need to fix today's 400. The relation between chars and tokens converges statistically for long sequences (already used in Minerva v1), so a conservative ratio (e.g. 3 chars/token) is a sufficient *guard*. Use tokens at the **boundary** (LLM call), keep chars in the chunker — each layer uses the unit that matches its job.

### Option E — Rigorous max-size contract in chunker + segmenter

This was the converging direction. Two observations led here:

1. **Char↔token ratio is reliable as an upper bound** for sequences over a few hundred tokens (law of large numbers). Conservative ratios per content type:

   | Content | chars/token (rough) |
   | --- | --- |
   | English prose | 3.8–4.2 |
   | Markdown with code | 3.0–3.5 |
   | Heavy code / JSON / URLs | 2.5–3.0 |
   | Italian / Latin-script non-English | 3.5–4.0 |
   | CJK / Cyrillic / emoji | 1.5–2.5 |

   Over-estimating tokens from chars (use 2.5–3.0) gives a safe upper bound. For Qwen2.5 32k context with ~2k reserved for system+output: budget ≈ 30k tokens × 3 chars/token ≈ **90k chars** safe input cap.

2. **The chunker already has a "brute-force" branch** ([DocumentChunker.cs:239-245](../../src/Minerva/Ingestion/DocumentChunker.cs#L239-L245)) that warns and passes through. If we flip that to *warn and slice anyway*, plus add the same recursive-fallback to the **segmenter** (which today has no such fallback), we get a hard ceiling guarantee.

**Pros**: contained change in `Minerva.Ingestion`. No tokenizer dependency, no config-schema break, no re-index. Architecturally honest — size enforcement at the layer that produces sized outputs.

**Cons**: brute-force slicing produces ugly mid-sentence cuts in the worst case. Acceptable trade vs. crashing.

**Verdict**: this is the chosen direction.

## 3. The Plan

Four numbered steps. Each one stands alone; together they form a defense-in-depth.

### 1. Preflight surfaces the LLM's effective context length

**Why**: today the pipeline has no idea what context the model is loaded with. We've been guessing.

**How**: extend the existing preflight ([OpenAICompatibleLlmProvider.cs:178-196](../../src/Minerva/Providers/OpenAICompatibleLlmProvider.cs#L178-L196)) to read context length from the server. Server-specific (the OpenAI contract doesn't expose it):

| Server | Endpoint |
| --- | --- |
| **LM Studio** | `GET /api/v0/models` → `loaded_context_length`, `max_context_length` |
| **llama.cpp** | `GET /props` → `n_ctx` |
| **Ollama** | `POST /api/show` → `parameters.num_ctx` |
| **vLLM** | `GET /v1/models` → `max_model_len` |

Read **loaded** context (what's actually allocated), not max (model capability). Fallback to a configured `Minerva:Llm:MaxContextTokens` if no server-specific endpoint matches. Surface a clear error if neither works.

### 2. Convert tokens → char budget with a conservative ratio

**Why**: keeps chunker/segmenter in chars (decoupled, fast, semantic-driven) while respecting a token-defined ceiling.

**How**: derive a `MaxInputChars` budget from the preflight value:

```text
MaxInputChars = (ContextTokens − ReservedForSystemAndOutput) × CharsPerToken × SafetyFactor
```

With `CharsPerToken ≈ 3.0` and `SafetyFactor ≈ 0.9`, headroom is generous enough to absorb chat-template overhead and content that's denser than English prose. Same logic for the embedder (BGE-M3 = 8192 tokens, etc.).

### 3. Tighten segmenter and chunker to enforce max-size as a contract

**Why**: today `SegmentDocument` has no upper bound on segment size; chunker has a "warn and emit oversized" escape hatch. Both leak.

**How**:

- `SegmentDocument` should guarantee `len(segment) ≤ MaxSegmentChars`:
  - First try: split by highest-level heading (today's logic).
  - If a section is still too big: reuse `RecursiveSplit` with separators `\n\n`, `\n`, `.`, etc.
  - If still too big: brute-force char slice with overlap.
- `SplitIntoChunks` (`RecursiveSplit`'s no-separator branch at [line 239-245](../../src/Minerva/Ingestion/DocumentChunker.cs#L239-L245)): change "warn and emit" to "warn and slice."
- Both `MaxSegmentChars` (LLM-derived) and `TargetChunkSize` (embedder-derived) come from step 2.

This makes the contract explicit: every output of segmenter and chunker is `≤ its budget`.

### 4. Token-aware safety belt at the LLM/embedder boundary — and what can still go wrong

**Why this exists even after 1–3**: 1–3 are all in *char* space with statistical guards. Step 4 catches what slips through. The user asked: "what can still go wrong at step 4?" — important to enumerate so we know what countermeasures step 4 itself needs.

**The safety belt**:

- Tokenize the final payload (using a known tokenizer in-process, or the server's `/tokenize` endpoint) and verify it fits.
- On 400, treat as a "shrink and retry" signal: halve the input once, retry. If it still fails, surface a structured error (document, size, budget) — not a stack trace.

**Residual failure modes at step 4 (after 1–3 in place)**:

#### A. Hidden tokens we didn't count

- **Chat template overhead**: `<|im_start|>system\n...<|im_end|>`. 20–80+ tokens per turn, model-dependent. Invisible to char counting.
- **System prompt**: small but constant.
- **Output reservation**: `max_tokens` reserved against context. If our input grows to fill the rest, prompt + max_tokens > context → 400.
- **Future tool/function metadata**: not used today, but tool schemas can be hundreds of tokens.

#### B. Char↔token ratio violated by content

The 3.0 rule is statistical. Outliers:

- CJK, emoji, rare Unicode (1–2 chars/token).
- Base64 / hex / random strings (no good BPE merges).
- Code-heavy markdown with long URLs, hashes.
- Repeated whitespace or non-ASCII punctuation.

A conservative ratio handles most cases; pathological content can still break it.

#### C. Reported context length is wrong

- Preflight read `max_context_length` (capability) but `n_ctx` (allocated) is smaller.
- Server reports total context; we treated it as input-only. Off by `max_tokens`.
- Model swap mid-session — LM Studio unloads/loads, "auto" routing. Cached preflight is stale.
- Different model serving the request than the one named (alias mapping).

#### D. Server-side rejection unrelated to size

- Sampling params out of range (temp, top_p, frequency_penalty).
- Empty or whitespace-only content after trimming.
- Malformed UTF-8 in source files.
- OpenAI fields the server doesn't support (`tool_choice`, `response_format`, `seed` on older builds).
- Content filter / safety rejection.

#### E. Server bugs / config issues

- **MLX tokenizer mismatch**: early MLX conversions ship a tokenizer config that disagrees with training. Reported context is correct; effective context is smaller because chat template tokenizes differently.
- **KV cache too small for declared context**: model says 32k, server allocated KV for 8k due to VRAM. Fails when upper range is used.
- Concurrent-request pressure → 500 (retried).
- Server OOM loading additional context → 500/503 (retried).

#### F. Network / transport

- Proxy size limits (corporate proxy, ngrok, nginx `client_max_body_size`).
- Connection drops mid-large-request → `HttpRequestException` (retried).
- Encoding round-trip / surrogate-pair issues if upstream read with wrong encoding.

#### G. Quality failure (not a 400)

A 0.5B model handed 25k tokens produces a confused, partial, or hallucinated summary. The pipeline succeeds; the downstream contextualization step gets garbage. Invisible to all the countermeasures, only shows up at query time.

**Where to set the bar**:

After 1–3 + tokenize-at-boundary, residual risk concentrates in **B (content outliers)**, **C (stale/inaccurate context info)**, and **E (server config drift)**. Mitigations:

- Tokenize the final payload precisely → catches B.
- "Shrink and retry" on 400 → catches C and E without needing perfect info.
- Structured error on giveup → operator can adjust caps without spelunking traces.

The 400 we saw is preventable by 1–3. The next class of 400, once those are in, will probably be C or E, and shrink-and-retry handles both.

## Confirmed in practice (after diagnostics rollout)

After adding the diagnostic wrap+rethrow chain, a second run produced:

```text
Ingestion failed for document 'it/2025-09 MacTree.md' (176503 chars):
  Summarization failed (input 176503 chars):
    LLM API request failed (HTTP 400):
      {"error":"The number of tokens to keep from the initial prompt is greater
       than the context length. Try to load the model with a larger context
       length, or provide a shorter input"}
```

Confirms the hypothesis: pure context-overflow, no other failure mode involved.

### Surprising finding about the failing document

`it/2025-09 MacTree.md` is **not** a headerless wall of text. It has **376 heading lines** with a healthy structure (one `#`, several `##`, many `###`/`####`/`#####`). It still went through the **Single path** — meaning the segmenter returned a single segment for a 176k-char document.

Cause: [`SplitIntoSegments`](../../src/Minerva/Ingestion/DocumentChunker.cs#L286-L307) only splits at the **highest-level heading present** (lowest level number — `#` here). Since this doc has exactly one `#`, the segmenter has exactly one split point and produces at most one big segment regardless of how richly the rest is subdivided.

This is a **common Obsidian / note-taking pattern**: one top-level title at the top, everything else under `##`/`###`. Any such doc — no matter how granular below — defeats the segmenter.

### The chunker already solved this exact bug — the segmenter didn't get the upgrade

[`SplitByHeaders`](../../src/Minerva/Ingestion/DocumentChunker.cs#L176-L210) (used by the chunker) collects **every** `HeadingBlock` regardless of level — 376 split points for MacTree.md, plenty of granularity to pack to budget. The chunker further has `RecursiveSplit` fallback (separator ladder: paragraph break, newline, sentence end, semicolon, space), heading-prefix carry, and tail absorption.

Two algorithms living in the same file, doing the same conceptual job (pack a document into pieces ≤ N), with only one of them actually robust. The segmenter is essentially a fossil of the chunker's earlier design.

### Step 3 refinement: unify on `SplitToBudget(text, maxChars, overlap)`

Rather than fixing `SplitIntoSegments` in isolation, the cleaner move is to **factor out a single shared core** parameterized by target size and overlap:

```csharp
SplitToBudget(text, maxChars, overlap = 0) → IReadOnlyList<string>
```

Then:

- `SplitIntoChunks(text)` ≡ `SplitToBudget(text, TargetChunkSize, ChunkOverlap)`
- `SegmentDocument(text)` ≡ `SplitToBudget(text, MaxSegmentChars, 0)`

Strategy (the chunker's current one, lifted up):

1. `SplitByHeaders` at every heading level.
2. Greedy pack into pieces ≤ `maxChars`.
3. Oversized → `RecursiveSplit` with the standard separator ladder.
4. No usable separator → brute-force char slice (the "warn and slice" upgrade for step 3).
5. Tail absorption.

Notes on the per-caller subtleties:

- **Overlap**: chunks need it (embedding continuity), segments don't (each summarized independently).
- **Heading-prefix carry**: useful at chunk scale (chunk readability at query time); harmless and inert at segment scale.
- **Tail absorption**: useful at both scales (avoid stub chunks; avoid wasted LLM calls on tiny tails).

Result: one algorithm, one bug surface, one place where future improvements (e.g., the eventual char→token migration) need to land.

## Files referenced

- [src/Minerva/Ingestion/IngestionPipeline.cs](../../src/Minerva/Ingestion/IngestionPipeline.cs)
- [src/Minerva/Ingestion/DocumentSummarizer.cs](../../src/Minerva/Ingestion/DocumentSummarizer.cs)
- [src/Minerva/Ingestion/DocumentChunker.cs](../../src/Minerva/Ingestion/DocumentChunker.cs)
- [src/Minerva/Models/ChunkingOptions.cs](../../src/Minerva/Models/ChunkingOptions.cs)
- [src/Minerva/Providers/OpenAICompatibleLlmProvider.cs](../../src/Minerva/Providers/OpenAICompatibleLlmProvider.cs)
- Run script: `../minerva2-run/run-obsidian-indexing-test-3-qwen2-5-test.sh`
- Config: `../minerva2-run/appsettings.qwen2-5-test.json`
