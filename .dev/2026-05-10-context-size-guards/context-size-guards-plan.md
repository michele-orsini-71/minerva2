# Context-Size Guards — Implementation Plan

Companion to [context-size-guards.md](context-size-guards.md). The design notes capture *what* we're doing and *why*; this file captures *how* we'll roll it out and *what decisions are still open*.

Goal: implement piece by piece without keeping the full design notes in context.

## Sequencing rationale

The crash confirmed in the design notes (`it/2025-09 MacTree.md`, 176k chars, single segment) is fixable by **step 3 of the design notes alone**, if we unify chunker+segmenter on a shared `SplitToBudget` core. The chunker already finds all 376 headings in that document; the segmenter only finds 1. So step 3 is the highest-leverage step and the right place to start. Steps 1, 2, 4 add resilience but don't change the fact that the segmenter is a fossil of an earlier chunker design.

Order of execution: **3 → 2 → 1 → 4** from the design notes, renamed below as **Phases A → B → C → D**.

| Phase | Maps to design step | Fixes today's crash? | Adds resilience for… |
| --- | --- | --- | --- |
| A | Step 3 (unify segmenter+chunker) | **Yes** | Future headerless / single-heading docs |
| B | Step 2 (token→char budget) | Tightens it | Operator-supplied ceiling honesty |
| C | Step 1 (preflight context length) | Automates B | Stops guessing model context |
| D | Step 4 (boundary safety belt) | No | Content outliers, stale info, server drift |

Phase A alone unblocks the qwen2.5 ingestion run. B–D are incremental.

---

## Phase A — Unify segmenter & chunker on `SplitToBudget`

**Goal**: every output of segmenter and chunker is `≤ its budget`, by contract.

### Verification (write the test first)

Regression test: feed a synthetic doc shaped like MacTree.md (≈176k chars, exactly one `#`, many `##/###/####`) through `SegmentDocument` with `MaxSegmentChars=N`. Assert every returned segment has length `≤ N`. Today this fails; after Phase A it passes.

A second test for the brute-force fallback: a 50k-char string with no separators at all (e.g. base64 blob) → assert all returned chunks `≤ N` and overlap is honored.

### Concrete moves in [DocumentChunker.cs](../../src/Minerva/Ingestion/DocumentChunker.cs)

1. Extract a private `SplitToBudget(text, maxChars, overlap)` from current [`SplitIntoChunks` (line 77-126)](../../src/Minerva/Ingestion/DocumentChunker.cs#L77-L126) — same algorithm, parameterized on size + overlap. Use `SplitByHeaders` (every level), greedy pack, `RecursiveSplit` fallback, brute-force slice as last resort, tail absorption.
2. Rewrite [`SegmentDocument` (line 40-46)](../../src/Minerva/Ingestion/DocumentChunker.cs#L40-L46) as `SplitToBudget(text, MaxSegmentChars, overlap: 0)`.
3. **Delete** [`SplitIntoSegments` (line 286-349)](../../src/Minerva/Ingestion/DocumentChunker.cs#L286-L349) entirely. It's redundant with the unified core.
4. Flip [`RecursiveSplit`'s no-separator branch (line 239-245)](../../src/Minerva/Ingestion/DocumentChunker.cs#L239-L245) from "warn and emit oversized" to "warn and brute-force slice with overlap." This is the contract closure: no path emits oversized.

### Open decisions before touching code

These are architectural choices (per global CLAUDE.md, "Do not introduce new classes, modules, files, abstractions, or design patterns without explicit approval").

- **D-A1: Visibility of `SplitToBudget`** — keep private and hide it behind the existing `SegmentDocument` / `Chunk` / `ChunkSegment` methods, or expose it on `IDocumentChunker` for external callers?
- **D-A2: Name** — `SplitToBudget`, `Pack`, `SplitToFit`, something else?
- **D-A3: Where does `MaxSegmentChars` come from in this phase?** Phase A in isolation needs *some* number. Options: hardcode a constant temporarily, reuse the existing `LargeDocumentThreshold` field with renamed semantics, or block Phase A until B is done. Cleanest is "reuse `LargeDocumentThreshold` as the temporary budget and rename in Phase B."

### Out of scope for Phase A

- Token-based budgets (Phase B).
- Server preflight (Phase C).
- LLM call retry logic (Phase D).
- Heading-prefix carry / tail absorption tuning at segment scale — leave as the chunker does today.

---

## Phase B — Char budget derived from a configured token ceiling

**Goal**: `MaxSegmentChars` (LLM-bound) and `TargetChunkSize` (embedder-bound) come from a single, explicit token-budget formula, not magic numbers.

### The formula

```text
MaxInputChars = (MaxContextTokens − ReservedTokens) × CharsPerToken × SafetyFactor
```

Reasonable defaults from the design notes:

- `CharsPerToken ≈ 3.0` (conservative — over-estimates tokens from chars; safe upper bound for English/Italian/markdown-with-code; will be wrong for CJK/base64 but that's caught by Phase D).
- `SafetyFactor ≈ 0.9` (absorbs chat-template overhead, denser-than-prose content).
- `ReservedTokens ≈ 512` (system prompt + max output tokens). Tune with the actual summarizer prompt size.

### Verification

With `MaxContextTokens=4096, Reserved=512, CharsPerToken=3.0, Safety=0.9`:

```text
MaxInputChars = (4096 − 512) × 3.0 × 0.9 = 9676.8 → 9676
```

Plug those numbers into `appsettings.qwen2-5-test.json` and re-run `run-obsidian-indexing-test-3-qwen2-5-test.sh`. MacTree.md must succeed (it failed before with a 4k-loaded context).

### Decisions D-B (resolved 2026-05-09)

- **D-B1 — settled:** new `ContextBudgetOptions` record as a `required` sub-option of `ChunkingOptions` (same nesting pattern as the existing `Llm` sub-option). `MaxSegmentChars` is removed entirely from `ChunkingOptions` and from every `appsettings*.json`; `ContextBudget` is the only way to express the LLM-bound budget.
- **D-B2 — settled:** embedder-side automation skipped. `TargetChunkSize` stays as a fixed int. BGE-M3's 8192-token ceiling is an order of magnitude above today's 1200-char chunks; automating gains nothing now and adds config surface.
- **D-B3 — settled:** the formula `(MaxContextTokens − ReservedTokens) × CharsPerToken × SafetyFactor` lives in **`DocumentChunker`** (the chunking engine, where the knowledge belongs), evaluated **once in the constructor** and cached in a private field. The binder's job is only to validate the four inputs (required, positive, ranges sensible). The post-`remove-di` equivalent of "DI registration" — the options binder — was rejected as putting one concern in the wrong place.

### Out of scope for Phase B

- Reading the server's actual context length (Phase C).
- Tokenizing actual payloads (Phase D).

---

## Phase C — Preflight validates server context length

**Goal**: catch operator misconfiguration of `MaxContextTokens` against what the server actually loaded. The probe **validates**, it does not override — `DocumentChunker` stays oblivious to probe outcome and the budget stays exactly what the operator wrote.

### Server matrix

| Server | Endpoint | Field read (loaded only) |
| --- | --- | --- |
| LM Studio | `GET /api/v0/models` | `data[i].loaded_context_length` (matched by `id`) |
| Ollama | `POST /api/show` | `num_ctx` line in the `parameters` string |
| llama.cpp | `GET /props` | `default_generation_settings.n_ctx`, falls back to top-level `n_ctx` |

Only "loaded" fields are read. `max_context_length` (capability) is deliberately ignored — a model that *can* hold 32k may be loaded at 2k, and validating against capability would silently pass configurations that crash at runtime. vLLM is deferred (its `max_model_len` is capability, not loaded).

### Decisions D-C (resolved 2026-05-09)

- **D-C1 — settled:** LM Studio + Ollama + llama.cpp. No `IContextLengthProbe` interface introduced — engine selection is a `ContextLengthProbe` enum on `LlmProviderOptions`, dispatch is a `switch` inside the provider. vLLM and any other engine is reachable via `ContextLengthProbe.None` (no probe attempted).
- **D-C2 — settled:** soft-fall-through. Probe HTTP failure or unrecognized response shape → `Warning` log + preflight passes. The probe is a safety check, not the source of truth; the operator's configured value remains the ceiling. Probe success but `loaded < configured` → `PreflightFailure` (hard fail).
- **D-C3 — settled:** probe runs once during `MinervaBuilder.RunPreflightAsync` (which itself runs once during `BuildAsync`). No re-probing. Operators who hot-swap models or change loaded context in the local server must restart Minerva.

### Operator surface

New required field on `LlmProviderOptions`:

```jsonc
"Llm": {
  "BaseUrl": "...",
  "Model": "...",
  "Concurrency": 1,
  "ContextLengthProbe": "LMStudio"  // LMStudio | Ollama | LlamaCpp | None
}
```

`None` is the catch-all for hosted providers (OpenAI, Anthropic via OpenAI-compat shim, Google via OpenAI-compat shim, Deepseek, Together, etc.) and any custom proxy. Same configure-and-trust behavior as Minerva v1; no probe is attempted, no log is emitted. Typos in the enum value fail at config-validation time.

### Wiring

- `OpenAICompatibleLlmProvider.PreflightAsync(int configuredMaxContextTokens, ILogger logger, CancellationToken ct)`:
  1. Existing model-availability check (`CheckAvailabilityAsync`). On failure → return `PreflightFailure("Llm", ...)` and short-circuit.
  2. If `ContextLengthProbe == None` → return null silently.
  3. Call `ProbeLoadedContextLengthAsync` (single HTTP call, ~5s timeout, swallows all errors as `null`).
  4. Probe returned `null` → `Warning` log "did not respond as expected; using configured value unverified" → return null.
  5. `loaded < configured` → return `PreflightFailure("Llm.ContextLength", ...)` with both numbers and remediation text.
  6. `loaded >= configured` → `Information` log "OK" → return null.
- `MinervaBuilder.RunPreflightAsync` reads `options.Chunking.ContextBudget.MaxContextTokens` and passes it in alongside an `ILogger<OpenAICompatibleLlmProvider>` from the configured factory.

### Phase C Verification

Unit-tested in [tests/Minerva.Tests/Providers/ContextLengthProbeTests.cs](../../tests/Minerva.Tests/Providers/ContextLengthProbeTests.cs):

- per-engine happy path (LMStudio matches by model id, Ollama parses `num_ctx` line, LlamaCpp reads either nested or top-level `n_ctx`)
- per-engine failure modes (404, malformed JSON, missing field, model-id mismatch, HTTP exception) all return `null`
- `Preflight_ProbedLessThanConfigured_ReturnsContextLengthFailure` proves the validation rule
- `Preflight_ProbeNone_DoesNotMakeHttpCall` proves silent skip
- `Preflight_ChatAvailabilityFails_ShortCircuitsBeforeProbe` proves short-circuit ordering

Live verification: run against LM Studio with qwen2.5 loaded at 4096 context, configured `MaxContextTokens: 4096`. Expect:

```text
info: Minerva.Providers.OpenAICompatibleLlmProvider
      LLM context-length probe (LMStudio) reports loaded=4096 tokens; configured MaxContextTokens=4096. OK.
```

Reduce LM Studio's loaded context to 2048, restart Minerva. Expect `MinervaStartupException` with stage `Llm.ContextLength` and a message naming both numbers ("2048 tokens, but ... configured to 4096; lower MaxContextTokens to <= 2048 or load the model with a larger context window").

---

## Phase D — Token-aware safety belt at the LLM boundary

**Goal**: catch what slips through A–C. The design notes enumerate residual failure modes (B content outliers, C stale context info, E server config drift). Implement the minimal version that handles all three.

### Minimal implementation

1. **Shrink-and-retry on HTTP 400** at the LLM call site. Halve the input once, retry. If still fails, throw a structured `IngestionException` with `{document path, input size, budget, server response}` — not a raw `ProviderUnavailableException` with a stack trace.
2. **Optional pre-tokenize** using the server's `/tokenize` endpoint if available (LM Studio exposes this). Skip if not. This catches Phase B's content-outlier risk (CJK, base64) before the round trip.

### Out of scope (per design notes)

- **In-process tokenizers** (Microsoft.ML.Tokenizers, vocab files, etc.). That's the "real project, weeks" path. Not on this plan.

### Phase D Verification

Synthetic test: feed a 100k-char base64 blob (worst-case char↔token ratio) through ingestion against a 4k-context model. Expect a structured `IngestionException` with all four fields — not a stack trace.

Live test: ingest `it/2025-09 MacTree.md` against LM Studio with qwen2.5 at 2048 context (deliberately undersized). With Phases A+B alone, expect failure. With D added, expect either success (shrink-retry worked) or a clean structured error.

### Open decisions

- **D-D1: Where the retry lives.** In `DocumentSummarizer`, `OpenAICompatibleLlmProvider`, or `IngestionPipeline`? Recommendation: in the LLM provider — it's the layer that sees the 400. The summarizer doesn't know what shrinking means.
- **D-D2: How much to shrink.** Halve once is the design-notes default. Could also be "shrink to 80% of `MaxInputChars` reported in the error if available." Halve is dumber and more robust. Recommendation: halve.
- **D-D3: Whether to wire `/tokenize` now or later.** It's optional. Recommendation: **later**. Get shrink-retry in first; add `/tokenize` only if Phase B+C don't catch enough.

---

## What I'd skip / defer

- **Embedder-side preflight pipeline.** The chunker already enforces `TargetChunkSize` for chunks; if we set that conservatively in Phase B (or just leave the existing 1200-char default that's well under BGE-M3's 8192-token ceiling), the existing logic suffices. No need to build a parallel embed-side preflight unless we hit a concrete embedder failure.
- **Heading-prefix carry / tail absorption changes at segment scale.** Design notes call them "harmless inert at segment scale." Leave as the chunker does today.
- **In-process tokenizer integration.** Out of scope per design notes.
- **All four server probes.** LM Studio only for Phase C.

---

## Suggested first slice

**Phase A only**, behind the regression tests described above. That alone fixes the observed crash and makes B–D incremental rather than blocking.

Before starting Phase A, decisions needed:

- D-A1: visibility of `SplitToBudget`
- D-A2: name
- D-A3: source of `MaxSegmentChars` for Phase A in isolation

Once A lands and the qwen2.5 run goes green, Phase B becomes a one-day rename + DI tweak; C is a per-server probe; D is a retry policy. None of them require holding A's diff in working memory.

---

## Files this plan will touch (forecast)

- [src/Minerva/Ingestion/DocumentChunker.cs](../../src/Minerva/Ingestion/DocumentChunker.cs) — Phase A (refactor + brute-force fallback).
- [src/Minerva/Models/ChunkingOptions.cs](../../src/Minerva/Models/ChunkingOptions.cs) — Phase B (new options or rename).
- [src/Minerva/Providers/OpenAICompatibleLlmProvider.cs](../../src/Minerva/Providers/OpenAICompatibleLlmProvider.cs) — Phase C (preflight) + Phase D (shrink-retry).
- `Minerva.Hosting` DI wiring — Phase B (formula evaluation).
- New test file(s) under `tests/` — Phases A, B, D verification.
- `appsettings.qwen2-5-test.json` and any other LLM configs — Phase B (new keys) or Phase C (defaults).
