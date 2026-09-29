# Eval

- `datasets/` — gold datasets (jsonl) and the corpus manifests (`*.corpus.txt`)
- `corpus-builder/` — builds a wikipedia corpus from a zim file; one config per
  corpus (`corpus-builder/corpus-wp1283.json`, `corpus-builder/corpus-wp111.json`)
- `collections/` — indexes the corpora into the eval collections (see its README)
- `experiments/` — sweep TOMLs and their runs, including the comparison with
  Minerva v1 in `legacy/` (see its README)
- `notebooks/` — analysis notebooks, the shared library they use
  (`minerva_eval.py`) and their Python env
- `scripts/` — small helpers (`smoke.py`)

Two corpora share the same layout: `wp1283` (full, 1283 articles) and `wp111`
(111 articles, fast loop). Collections are named `{corpus}-nollm`; the suffix
dates from the contextualization experiment and is kept so existing runs stay
comparable.

## Everyday loop

Search code changed (no corpus/index changes needed):

```sh
# run the sweeps, from the experiment folder
cd experiments/baseline
dotnet run --project ../../../src/Minerva.Search.Bench -- run --sweep wp1283-nollm.toml --out runs \
  --config ../../../src/Minerva.Search.Bench/appsettings.json

# analyze (uses the shared notebooks env, no activation needed)
uv run --project ../../notebooks jupyter lab
```

`run-all-sweeps.sh` runs every durable sweep in sequence, after checking that
the configured rerankers answer.

If the indexing code or chunking config changed, re-index first, from `collections/`.
Pass `--config appsettings.json`: without it the tools read
`~/.config/minerva/<executable>.json`, your personal config. The paths inside
the config are relative, so the working directory must be `collections/`:

```sh
cd collections
DOTNET_ENVIRONMENT=wp111-nollm dotnet run --no-launch-profile --project ../../src/Minerva.MarkdownIndexer \
  -p:RunWorkingDirectory="$PWD" -- --config appsettings.json
```

`DOTNET_ENVIRONMENT` selects the `appsettings.<name>.json` overlay next to the
config file, one per collection. For long overnight runs, build the single-file
binary with `scripts/build-minerva-markdown-indexer-cli.sh`, copy it into
`collections/` as `minerva-markdown-indexer` and run
`DOTNET_ENVIRONMENT=<name> ./minerva-markdown-indexer --config appsettings.json`
instead: the run then does not pick up source changes made while it is in progress.

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
