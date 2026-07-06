# Plan — auto-discover adversarial clusters for the eval corpus

## Goal

The wikipedia eval corpus (~1100 articles) is a random spread of unrelated topics.
Every gold document sits alone in its semantic neighborhood, so the retriever returns
it at rank #1 no matter how convoluted the question is. To create genuinely hard
retrieval cases we need **clusters**: groups of mutually-similar articles that compete
for the top rank.

This tool discovers such clusters automatically. It embeds every article in the ZIM,
finds dense pockets in embedding space, scores them, and emits a manifest of chosen
members. The existing `buildCorpus.py` then extracts them into the corpus.

## Chosen design

- All new code lives in `tools/kiwix2md/`, reusing `extract_utils.py` (`is_article`,
  `load_config`, HTML→text) and `corpus.json`.
- Stages communicate through **on-disk artifacts** (a lead-text dump, then an embeddings
  file) so each stage re-runs without recomputing the previous one.
- Extraction is NOT reimplemented: the picker emits manifest paths, and the existing
  `buildCorpus.py` does the ZIM→markdown extraction.
- Discovery uses embedding proximity, not an LLM proposing titles. Rejected the
  LLM-propose-then-intersect approach because the `top_nopic` ZIM is a popular subset —
  proximity search finds the neighbors that actually exist and ranks them by real
  semantic closeness.

## Working principle

Vertical slices, not horizontal layers. Each slice compiles, runs, and produces
something observable on its own. Data shapes are born inside the slice that first needs
them — no types or contracts defined up front.

## Slices

- [x] **1. Read the ZIM** — enumerate articles, pull each title/path + lead 5-10 lines.
      verify: prints article count and a few sample lead texts.
- [ ] **2. Embed + one seed lookup** — embed all lead texts, save vectors, print k
      nearest neighbors of one hardcoded seed (e.g. Jaguar).
      verify: Jaguar → Leopard, Tiger, Lion. Premise validated.
- [ ] **3. Seed → neighborhood** — take any seed title as an argument, print its top-k
      neighbors with distances.
      verify: interactive "look around" from any named article.
- [ ] **4. Auto-discover clusters** — density-cluster all vectors, print candidate
      clusters and members.
      verify: a list of clusters, no seed needed.
- [ ] **5. Score + rank** — add size, tightness, isolation-from-existing-corpus; print
      the ranked shortlist.
      verify: top candidate clusters worth building questions around.
- [ ] **6. Emit manifest + extract** — for a chosen cluster, append its paths to the
      manifest and run `buildCorpus.py`.
      verify: new markdown files appear in `wikipedia-en-corpus`.

## Decisions deferred to their slice

- **Embedding model** (slice 2) — should match Minerva's retriever so "near here" means
  "confusable at query time". Default: use the retriever's model; fall back to a
  stand-in sentence-transformer first if it is not trivially callable from Python.
- **Clustering method** (slice 4) — HDBSCAN vs k-NN local-density ranking. Default:
  HDBSCAN (no radius or cluster-count to pick, labels outliers).
- **Two goals** (slices 5-6) — densify around existing golds vs build a fresh isolated
  cluster. Default: do one isolated cluster first (cleanest controlled case, no risk to
  existing gold labels).
- **Title → ZIM path resolution** (slice 6) — manifest stores ZIM paths, not titles;
  handle redirects, spaces/underscores, casing. Filenames follow
  `entry.path.replace("/", "_") + ".md"` and `check_integrity` enforces manifest ↔ files.

## Current status

Plan written. Nothing built yet. Next: slice 1 — read the ZIM and dump lead lines.
