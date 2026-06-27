# Wikipedia eval corpus — `wikipedia-v1`

A reproducible corpus of English Wikipedia articles for the `wikipedia-v1`
retrieval evaluation. The corpus is the searchable document set; the eval
dataset defines the queries and their expected gold articles.

- **`wikipedia-v1.jsonl`** — 32 queries, each with `gold_sources` (the articles
  that should be retrieved).
- **`wikipedia-v1.corpus.txt`** — the corpus manifest: one ZIM entry path per
  line (500 articles). **This is the authoritative list** of what the corpus
  contains. Gold articles and distractors are not distinguished here — the gold
  set lives only in the `.jsonl`. Distractors create retrieval competition; the
  manifest lets any article become gold in a later eval.

## Source

The whole corpus is extracted from a single Kiwix ZIM:

| | |
| --- | --- |
| File | `wikipedia_en_top_nopic_2026-06.zim` |
| sha256 | `b2806831e14690cbcafeb1b6e7bd4439fd59b3e5fbeaeb300a5792dece510ee0` |
| Download | <https://lb.download.kiwix.org/zim/wikipedia/wikipedia_en_top_nopic_2026-06.zim> |
| License | Wikipedia text: CC BY-SA 4.0 |

The **sha256 is the identity anchor**, not the filename. Kiwix rotates monthly
builds out of the main directory into its archive, so if the URL 404s, find the
file with this hash in the Kiwix archive / library.

## Rebuild the corpus

Tools are in `tools/kiwix2md/`. They all read one config, `corpus.json`:

```json
{
  "zim":        "<path to the ZIM above>",
  "zim_sha256": "b2806831e14690cbcafeb1b6e7bd4439fd59b3e5fbeaeb300a5792dece510ee0",
  "manifest":   "<path to wikipedia-v1.corpus.txt>",
  "output":     "<path to the corpus output folder>"
}
```

1. Download the ZIM and verify its sha256 (`shasum -a 256 <file>`).
2. Edit `corpus.json` to your local paths.
3. Rebuild every article from the manifest:
   ```sh
   python buildCorpus.py --config corpus.json
   ```
   Each entry is written as `<entry_path with "/" → "_">.md`.
4. Verify the result matches the manifest exactly:
   ```sh
   python extract.py --config corpus.json
   ```
   `integrity OK` means the output folder contains all and only the manifest
   entries, and the ZIM hash matches.

## Growing the corpus

`randomKiwi2md.py <N> --config corpus.json` adds `N` new random articles (not
already present), extracts them, and appends their entry paths to the manifest.
It verifies integrity before and after, so the manifest and the folder stay in
lockstep. Reproducibility is unaffected: re-running `buildCorpus.py` against the
grown manifest reproduces the larger corpus exactly.
