# Eval

- `datasets/` — gold datasets (jsonl) and the corpus manifest (`*.corpus.txt`)
- `corpus-builder/` — builds the wikipedia corpus from a zim file (see `corpus.json`)
- `collections/` — indexes the corpus into the eval collections (see its README)
- `experiments/` — sweep TOMLs, runs, and analysis notebooks (see its README)
- `notebooks/` — shared analysis library (`minerva_eval.py`) and its Python env

## Everyday loop

Search code changed (no corpus/index changes needed):

```sh
# run the sweeps, from the experiment folder
cd experiments/baseline
dotnet run --project ../../../src/Minerva.Search.Bench -- run --sweep wikipedia-nollm.toml --out runs
dotnet run --project ../../../src/Minerva.Search.Bench -- run --sweep wikipedia-qwen2-5.toml --out runs

# analyze (uses the shared notebooks env, no activation needed)
uv run --project ../../notebooks jupyter lab
```

If the indexing code or chunking config changed, re-index first, from `collections/`:

```sh
./run-wikipedia-nollm.sh; ./run-wikipedia-qwen2-5.sh
```

## Dataset changed

When gold entries are added to a dataset, the corpus and collections must catch up.
From `corpus-builder/`:

```sh
# add noise neighbors for the new gold sources to the manifest
./run_densify_corpus_on_eval

# sync the corpus folder with the manifest (verifies zim sha + manifest first)
uv run python build_corpus.py --config corpus.json
```

Then re-index the collections and re-run the sweeps as above. Runs made against
the previous corpus are no longer comparable — regenerate the baselines.

`uv run` resolves each folder's own env; no `source .venv/bin/activate` anywhere.
