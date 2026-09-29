# Eval collections

This folder builds the collections used by the eval sweeps:

- `appsettings.json` — shared base config: connection string, embedding endpoint,
  chunking, and indexer settings common to all collections.
- `appsettings.<collection>.json` — per-collection overlay: collection name,
  corpus root and log file.
- `run-all-ingestions.sh` — one command per collection, cheapest first, using a
  frozen published binary copied into this folder. Uncomment the collections to
  rebuild.

Run the indexer from this folder with `--config appsettings.json` and
`DOTNET_ENVIRONMENT=<collection>`, which loads the matching overlay. Without
`--config` the indexer reads `~/.config/minerva/minerva-markdown-indexer.json`,
your personal config. The paths inside the config files are relative to the
working directory, so it must be this folder.

These appsettings files are **live config**, versioned on purpose: they are the record of
how each eval collection was built. The `appsettings*.json` files inside
`src/Minerva.MarkdownIndexer` are examples (plus the debug profile used by
`launchSettings.json`) and are not used by eval runs.

The corpus in `wikipedia-en-corpus/` is built by `../corpus-builder` from the manifest in
`../datasets`. Both `wikipedia-en-corpus/` and `logs/` are gitignored.
