# Eval

All tools and scripts to prepare evaluation pipelines are here.
To run the evaluation pipelines use Minerva.Search.Bench.
Evals are based on two corpora, both extracted from a Wikipedia zim file.
The small corpus is a subset of the bigger one and has been created for
fast checks:

- wp1283:  1283 articles
- wp111: 111 articles

The "nollm" suffix in collection names comes from the contextualization experiment
(it means: no LLM, so no contextualization on this collection) and has been
kept so existing runs stay comparable.

## Subfolders description

### datasets

Evaluations: jsonl files that contain queries and gold sources
Corpus manifests: `*.corpus.txt` lists of files to build the new corpus, used by
the corpus builder

### corpus-builder

Python script to build a Wikipedia corpus from a zim file; config files for
wp1283 and wp111 are versioned in the same folder

### collections

Contains configuration files to run Minerva.MarkdownIndexer and ingest/index
the corpus markdown files into collections needed for running the evals
 (see collections README)

### experiments

Sweep files to run Minerva.Search.Bench; they output runs folders to be analyzed
with Jupyter notebooks.
Includes the comparison with legacy Minerva in `legacy/`.

### notebooks

Analysis notebooks along with the shared library they use
  (`minerva_eval.py`) and their Python env.

`runs_comparer.ipynb` is committed with its outputs (executed on 2026-10-01),
so the results can be read without running the evals. It compares the
versioned runs:

- `legacy`: legacy Minerva (semantic search only)
- `baseline`: Minerva hybrid search (vector + BM25), without reranker; in other
  words, very similar to legacy Minerva enhanced with keyword search
- `reranker`: Minerva hybrid search followed by the cross-encoder reranker

## Everyday loop

If the search code changed, no corpus/index changes are needed, just rerun the sweep:

```sh
# run the sweeps, from the experiment folder
cd experiments/baseline
dotnet run --project ../../../src/Minerva.Search.Bench -- run --sweep wp1283-nollm.toml --out runs \
  --config ../../../src/Minerva.Search.Bench/appsettings.json

# analyze (uses the shared notebooks env, no activation needed)
uv run --project ../../notebooks jupyter lab
```

`run-all-sweeps.sh` runs every sweep in sequence, after checking that
the configured rerankers answer.

If the indexing code or chunking config changed, re-index first, from `collections/`.
Be sure to pass `--config appsettings.json`, otherwise Minerva.MarkdownIndexer will
read `~/.config/minerva/<executable>.json` instead.
The paths inside
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
