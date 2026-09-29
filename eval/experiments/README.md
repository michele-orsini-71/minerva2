# Experiments

One folder per experiment. Each contains:

- one or more sweep `.toml` files (input to Minerva.Search.Bench)
- `runs/` — resultsets produced by the bench (`run.json`, `metrics.csv`, `details.jsonl`)

`legacy/` is different: it holds Python scripts that index the same corpus with
Minerva v1 and write its results in the same `runs/` format, so the two can be
compared.

The analysis notebooks live in `../notebooks` (`runs_comparer.ipynb`,
`query_quality.ipynb`), with their logic in the shared library
`../notebooks/minerva_eval.py`. A parameters cell at the top of each notebook
declares which runs it loads, e.g. `load_all_results([Path("../experiments/baseline/runs")])`.

`run-all-sweeps.sh` runs every durable sweep in sequence, with the bench config
in `src/Minerva.Search.Bench/appsettings.json` (plus an optional overlay chosen
by `DOTNET_ENVIRONMENT`).

Two kinds of experiments, same folder shape:

- **Durable** (baseline, reranker, legacy): `runs/` is committed. They witness
  the measured performance and serve as stable comparison targets for other
  experiments.
- **Scratch** (`scratch-*` folders, gitignored): intermediate attempts. Delete when done,
  or promote by renaming and committing the runs.

Sweep `dataset` paths are resolved relative to the toml file: `../../datasets/...`.
