# Experiments

One folder per experiment. Each contains:

- one or more sweep `.toml` files (input to Minerva.Search.Bench)
- `analysis.ipynb` — the notebook that analyzes this experiment's runs
- `runs/` — resultsets produced by the bench (`run.json`, `metrics.csv`, `details.jsonl`)

Notebooks are thin: a parameters cell at the top declares which runs they load, the logic
lives in the shared library `../notebooks/minerva_eval.py` (added to `sys.path` in the
first cell). By default a notebook loads its own `runs/`; comparisons against another
experiment pass explicit roots, e.g. `load_all_results([Path("../baseline/runs")])`.

Two kinds of experiments, same folder shape:

- **Durable** (baseline, reranker): `runs/` and the executed notebook
  are committed. They witness the measured performance and serve as stable comparison
  targets for other experiments.
- **Scratch** (`scratch-*` folders, gitignored): intermediate attempts. Delete when done,
  or promote by renaming and committing the runs and the executed notebook.

Sweep `dataset` paths are resolved relative to the toml file: `../../datasets/...`.
