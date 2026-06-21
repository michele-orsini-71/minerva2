---
slug: wikipedia-zim-conversion-plan
title: Wikipedia ZIM → Markdown corpus — conversion plan
status: active
parent: phase-1-progress.md
---

# Wikipedia ZIM → Markdown corpus — conversion plan

A bird's-eye, flow-first checklist for turning a kiwix ZIM file into the
Markdown corpus that Phase 1D ingests. The plan follows the data as it moves
(ZIM file → entries → text → files → collection → query), and every step ends
in something you can **run and observe**, so the work can be done one increment
at a time.

## Decision (already taken)

- **Approach A:** convert the ZIM to `.md` files on disk, then ingest with the
  existing `MarkdownIndexer`. No native .NET ZIM reader.
- The converter is a Python tool, moved **inside minerva2** and evolved here.
- The kiwix `.zim` file is the documented public corpus; the converter +
  indexer are the documented ingestion procedure.

## Invariants the whole plan must protect

- **Markdown structure must survive.** The chunker splits on Markdown
  headings; raw HTML defeats it. Conversion quality is the core of the work,
  not a finishing touch.
- **`source_id` must be stable.** The eval dataset's `gold_sources` bind to
  whatever `source_id` scheme this converter produces. Lock that scheme before
  authoring any queries, and keep it stable across re-runs.

---

## Steps (flow order — runnable after each)

### 1. Move the tool in and make it run

Bring the Python project into minerva2 and confirm it executes here against the
top-100 ZIM, unchanged.

- **Run/observe:** the existing char-count script prints a number.
- **Lock:** where the tool lives in the repo, and how it is invoked.

### 2. Enumerate the corpus

Walk the ZIM and list the real article entries — skip redirects, keep only the
HTML content entries.

- **Run/observe:** a count and a sample of entry paths/titles.
- **Lock:** the filter rule for "this is an article" (this is the corpus
  boundary).

### 3. Convert one article

Take a single entry, pull its HTML, convert to Markdown, strip the MediaWiki
chrome (navigation, infobox, references, category footers, edit links).

- **Run/observe:** one article's Markdown printed to screen; read it and judge
  quality (headings present, body clean, no boilerplate).
- **Lock:** the conversion + cleanup approach. This is the riskiest step;
  isolate and inspect it before scaling.

### 4. Plan the file layout (dry run)

Decide how an entry path maps to an output file path, and therefore to a
`source_id`. Print the planned mapping for every entry without writing files.

- **Run/observe:** the full planned file tree on screen.
- **Lock:** the `source_id` scheme. Do not change it after dataset authoring
  begins.

### 5. Write the full corpus to disk

Apply steps 3–4 to every entry: write one `.md` per article, with a title in
frontmatter, in the planned layout.

- **Run/observe:** a directory of Markdown files you can open and read.
- **Lock:** the output directory location (it becomes the indexer root path).

### 6. Record corpus provenance

Capture the facts the corpus README needs: the kiwix download URL, the ZIM
content hash, the license, and the article/file counts.

- **Run/observe:** the provenance facts printed or written next to the corpus.
- **Note:** this feeds the Phase 1D corpus README requirement.

### 7. Ingest with MarkdownIndexer

Point the existing indexer at the converted directory and ingest into a fresh,
named collection. Start without contextualization to verify the path quickly,
then re-ingest with `qwen2.5` per the Phase 1D decision.

- **Run/observe:** indexer reports added/updated counts; collection exists.
- **Note:** reuses the tested ingest path — no new ingestion code.

### 8. Smoke-test retrieval

Run a few queries against the new collection (via the CLI or the bench's
`author-dataset` flow) and confirm hits come back and their `source_id` values
match the scheme from step 4.

- **Run/observe:** ranked hits with resolvable `source_id`s.
- **Gate:** if source_ids do not resolve, fix step 4 before authoring the
  dataset.

---

## Where this hands off

After step 8 the corpus is ingested and queryable. The remaining Phase 1D
deliverables — the hand-curated eval dataset, the corpus README, the committed
baseline, and the notebook — are already specified in `phase-1-spec.md` and
tracked in `phase-1-progress.md`. This plan stops at "a documented, ingested,
queryable Wikipedia collection."
