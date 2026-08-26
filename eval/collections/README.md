# Eval collections

This folder builds the collections used by the eval sweeps. Each collection has a
`run-*.sh` script and an `appsettings.*.json` overlay:

- `appsettings.json` — shared base config: connection string, embedding endpoint,
  chunking, and the corpus root (`./wikipedia-en-corpus`).
- `appsettings.<collection>.json` — per-collection overlay: collection name and log file.
- `run-<collection>.sh` — runs the indexer with `DOTNET_ENVIRONMENT=<collection>` so the
  matching overlay is loaded.

The scripts `cd` into this folder first; the indexer loads config from the working
directory, so all paths here are relative to this folder.

These appsettings files are **live config**, versioned on purpose: they are the record of
how each eval collection was built. The `appsettings*.json` files inside
`src/Minerva.MarkdownIndexer` are examples (plus the debug profile used by
`launchSettings.json`) and are not used by eval runs.

The corpus in `wikipedia-en-corpus/` is built by `../corpus-builder` from the manifest in
`../datasets`. Both `wikipedia-en-corpus/` and `logs/` are gitignored.
