# Eval

- `datasets/` — gold datasets (jsonl) and the corpus manifests (`*.corpus.txt`)
- `corpus-builder/` — builds a wikipedia corpus from a zim file (see `corpus-wp1283.json`, `corpus-wp111.json`)
- `collections/` — indexes the corpora into the eval collections (see its README)
- `experiments/` — sweep TOMLs, runs, and analysis notebooks (see its README)
- `notebooks/` — shared analysis library (`minerva_eval.py`) and its Python env

Two corpora share the same layout: `wp1283` (full, 1283 articles) and `wp111`
(111 articles, fast loop). Names follow `{corpus}-{context}`, where context is
`nollm` (no contextualization) or the model alias of the contextualizing LLM
(`e2b`, `q317b`, ...).

## Everyday loop

Search code changed (no corpus/index changes needed):

```sh
# run the sweeps, from the experiment folder
cd experiments/baseline
dotnet run --project ../../../src/Minerva.Search.Bench -- run --sweep wp1283-nollm.toml --out runs

# analyze (uses the shared notebooks env, no activation needed)
uv run --project ../../notebooks jupyter lab
```

If the indexing code or chunking config changed, re-index first, from `collections/`.
The indexer reads its config from the working directory, so the working directory
must be `collections/`:

```sh
cd collections
DOTNET_ENVIRONMENT=wp111-nollm dotnet run --no-launch-profile --project ../../src/Minerva.MarkdownIndexer -p:RunWorkingDirectory="$PWD"
```

`DOTNET_ENVIRONMENT` selects the `appsettings.<name>.json` overlay, one per collection.
For long overnight runs, build the single-file binary with
`scripts/build-markdown-indexer-cli.sh`, copy it into `collections/` as
`markdown-indexer` and run `DOTNET_ENVIRONMENT=<name> ./markdown-indexer` instead:
the run then does not pick up source changes made while it is in progress.

## Dataset changed

When gold entries are added to a dataset, the corpus and collections must catch up.
From `corpus-builder/`:

```sh
# add noise neighbors for the new gold sources to the manifest
./run_densify_corpus_on_eval

# sync the corpus folder with the manifest (verifies zim sha + manifest first)
uv run python build_corpus.py --config corpus-wp1283.json
```

Then re-index the collections and re-run the sweeps as above. Runs made against
the previous corpus are no longer comparable — regenerate the baselines.

`uv run` resolves each folder's own env; no `source .venv/bin/activate` anywhere.
