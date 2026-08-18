# Plan — densify the eval corpus around existing gold sources

## Goal

The wikipedia eval corpus (~1100 articles) is a random spread of unrelated topics.
Every gold document sits alone in its semantic neighborhood, so the retriever returns
it at rank #1 no matter how convoluted the question is. The eval reaches its maximum
and can't show retrieval improvements.

The fix is not to discover clusters anywhere — it is to put **confusable competitors
next to each existing gold**. The eval queries point at the golds, so the hard cases
come from adding near-neighbors of those specific articles. This tool embeds every
article in the ZIM, then for each gold source finds its nearest neighbors and adds
them to the manifest. The existing `buildCorpus.py` then extracts them into the corpus.

Rejected general clustering: it finds dense pockets anywhere in embedding space,
including neighborhoods with no gold and no query — work that does nothing for
the eval.

## Chosen design

- All new code lives in `tools/kiwix2md/`, reusing `extract_utils.py` (`is_article`,
  `load_config`, HTML→text) and `corpus.json`.
- Stages communicate through **on-disk artifacts** (a lead-text dump, then an embeddings
  file) so each stage re-runs without recomputing the previous one.
- Extraction is NOT reimplemented: the picker emits manifest paths, and the existing
  `buildCorpus.py` does the ZIM→markdown extraction.
- Neighbor search uses embedding proximity, not an LLM proposing titles. Rejected
  the LLM-propose-then-intersect approach because the `top_nopic` ZIM is a popular
  subset — proximity search finds the neighbors that actually exist and ranks them
  by real semantic closeness.
- Seeds are the existing dataset golds, looked up by path in the embeddings file,
  so no re-extraction or re-embedding is needed and the vector matches the corpus.

## Working principle

Vertical slices, not horizontal layers. Each slice compiles, runs, and produces
something observable on its own. Data shapes are born inside the slice that first needs
them — no types or contracts defined up front.

## Slices

- [x] **1. Read the ZIM** — enumerate articles, pull each title/path + lead 5-10 lines.
      verify: prints article count and a few sample lead texts.
- [x] **2. Embed + one seed lookup** — embed all lead texts, save vectors, print k
      nearest neighbors of one hardcoded seed (e.g. Jaguar).
      verify: Jaguar → Leopard, Tiger, Lion. Premise validated.
- [ ] **3. Seed → neighborhood** — a reusable `neighbors(seed_path, k)` function:
      look up the seed vector by path in the embeddings file, cosine-sim against
      all, return top-k `(path, score)` excluding the seed itself. CLI prints them.
      verify: interactive "look around" from any named article.
- [ ] **4. Densify around golds** — read the dataset, collect gold source paths, run
      `neighbors` for each, union and dedup against the existing manifest, keep only
      neighbors above a similarity threshold (capped at k).
      verify: prints, per gold, the new neighbor paths it would add.
- [ ] **5. Emit manifest + extract** — append the chosen neighbor paths to the
      manifest and run `buildCorpus.py`.
      verify: new markdown files appear in `wikipedia-en-corpus`.
- [ ] **6. Spot-check gold labels** — for a sample of golds that got new neighbors,
      confirm the neighbor does not also answer the gold's eval query.
      verify: no query becomes answerable by more than its labelled gold.
- [ ] **7. Update docs** — the corpus-building process should be reported in the docs.

Dropped: general clustering (old slices 4–5, HDBSCAN + scoring). It finds pockets
anywhere, not around the golds and their queries — off-goal.

## Decisions deferred to their slice

- **Embedding model** (slice 2) — should match Minerva's retriever so "near here" means
  "confusable at query time". Default: use the retriever's model; fall back to a
  stand-in sentence-transformer first if it is not trivially callable from Python.
- **Similarity threshold + k** (slice 4) — how close a neighbor must be to count,
  and how many per gold. A gold in a sparse region should get few or none, not k
  weak distractors. Tune by eyeballing slice-3 output.
- **Gold → embedding path mapping** (slice 4) — golds are filenames (`Joan_of_Arc.md`);
  embeddings key on ZIM path (`Joan_of_Arc`). Filenames follow
  `entry.path.replace("/", "_") + ".md"`. `top_nopic` paths look flat, so stripping
  `.md` should recover the path — verify against the actual embeddings file, since
  the `_`→`/` reversal is ambiguous if any path contains a slash.
- **Gold-label risk** (slice 6) — a too-close neighbor may also answer an existing
  query, making its single `gold_sources` label wrong. Queries target each article's
  specific facts, so this is usually safe, but must be spot-checked.
- **Manifest ↔ files integrity** (slice 5) — `check_integrity` enforces that manifest
  entries and extracted `.md` files match one-to-one; run it after extraction.

