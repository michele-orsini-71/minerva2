# Installation

How to set up Minerva on a Mac so that an MCP client (Claude Desktop, LM
Studio) can search your Markdown notes. The result:

- the executables in `~/bin`, side by side;
- their config files in `~/.config/minerva/`, logs in
  `~/.local/state/minerva/logs/`;
- PostgreSQL with `pgvector` and `pg_search` as the store;
- llama-swap as a login service on `localhost:9930`, starting the embedding
  and reranker models on demand and unloading them after 15 idle minutes.

The commands are for macOS on Apple Silicon with Homebrew. On other platforms
the steps are the same, the commands are not.

Follow the steps in order: the models must be downloaded before llama-swap
runs, llama-swap and PostgreSQL must run before the first indexing, and the
MCP server is useful only once a collection exists.

## 1. Prerequisites

- [Homebrew](https://brew.sh).
- .NET 10 SDK, from the [official download page](https://dotnet.microsoft.com/download).
  The executables are built framework-dependent, so the .NET runtime must
  stay installed.
- `~/bin` on your `PATH`:

  ```sh
  mkdir -p ~/bin
  echo 'export PATH="$HOME/bin:$PATH"' >> ~/.zshrc   # skip if already there
  ```

## 2. Get the repository

```sh
git clone <repository-url> minerva2
cd minerva2
```

All later commands run from the repository root.

## 3. Build the executables into `~/bin`

Each tool is published as a single file, so the tools do not mix their
libraries in `~/bin`:

```sh
for p in Minerva.Search.Cli Minerva.Mcp Minerva.MarkdownIndexer; do
  dotnet publish src/$p -c Release -r osx-arm64 --self-contained false \
    -p:PublishSingleFile=true -p:DebugType=none -o ~/bin
done
```

This installs `minerva-search`, `minerva-mcp` and `minerva-markdown-indexer`.
Add `Minerva.Search.Bench` to the list only if you run evaluations.

Check: `minerva-search --version`.

## 4. Install llama.cpp and llama-swap

```sh
brew install llama.cpp
brew install mostlygeek/llama-swap/llama-swap
```

`llama-server` (from llama.cpp) serves one model. `llama-swap` is a proxy on
a single port that starts the right `llama-server` for each request, based on
the request's model name.

## 5. Download the three models

llama-swap starts the models with `--offline`, so they must already be in
the local cache. Download each one by starting it once; when the log says
the server is listening, stop it with Ctrl+C:

```sh
llama-server -hf gpustack/bge-m3-GGUF:Q8_0 --embeddings --port 9999
llama-server -hf gpustack/bge-reranker-v2-m3-GGUF:Q8_0 --reranking --port 9999
llama-server -hf Voodisss/Qwen3-Reranker-4B-GGUF-llama_cpp:Q8_0 --reranking --port 9999
```

| Model | Role |
| --- | --- |
| `bge-m3` | embeddings (dense vectors for chunks and queries) |
| `bge-reranker-v2-m3` | reranker: rescores the fused candidates |
| `Qwen3-Reranker-4B` | cascade reranker: rescores the top of the reranked list; slower, stronger |

## 6. Install PostgreSQL and its extensions

```sh
brew install postgresql@18 pgvector
brew services start postgresql@18
```

`pg_search` (ParadeDB, the BM25 keyword search) has no Homebrew formula:

1. Download the pkg matching the Postgres major version and macOS codename
   from [ParadeDB releases](https://github.com/paradedb/paradedb/releases)
   (e.g. `pg_search@18--<version>.arm64_tahoe.pkg`) and run
   `sudo installer -pkg <file> -target /`.
2. Add `pg_search` to `shared_preload_libraries` in
   `$(brew --prefix)/var/postgresql@18/postgresql.conf` (the setting is one
   comma-separated list).
3. `brew services restart postgresql@18`.

Since pg_search v0.25, `pgvector` must be installed before it.

Then create the role, the database and the extensions, as a superuser, with
the scripts in [src/Minerva/sql-scripts](../../src/Minerva/sql-scripts/)
(order and details in its README, section Bootstrap):

1. `create-minerva-role.sql` (replace `<password>`);
2. `create-database-minerva.sql`;
3. connected to the `minerva` database: `create-vector-extension.sql` and
   `create-pgsearch-extension.sql`. `pg_search` is not a trusted extension,
   so the `minerva` role cannot create it.

pg_search 0.25.5 and 0.25.6 have a bitmap intersection bug that breaks
Minerva's BM25 queries. With those versions, also run
`disable-pgsearch-bitmap-intersection.sql` against the database; Minerva's
startup check refuses to run until you do.

Minerva creates its tables on first use. A missing or disabled extension
stops it at startup with a message naming the step to take.

## 7. Prepare the config and log folders

```sh
mkdir -p ~/.config/minerva ~/.local/state/minerva/logs

cp scripts/llama-swap.yml                       ~/.config/minerva/llama-swap.yml
cp src/Minerva.Search.Cli/appsettings.json      ~/.config/minerva/minerva-search.json
cp src/Minerva.Mcp/appsettings.json             ~/.config/minerva/minerva-mcp.json
cp src/Minerva.MarkdownIndexer/appsettings.json ~/.config/minerva/minerva-markdown-indexer.json
```

In each of the three tool configs, set `Minerva:ConnectionString` to the
password you gave the `minerva` role. The endpoints already point to
llama-swap on `localhost:9930`, and the indexer already logs to
`~/.local/state/minerva/logs/`.

How the tools find their config: each one reads
`~/.config/minerva/<executable name>.json`, or the file given with
`--config <path>`. When `DOTNET_ENVIRONMENT=<env>` is set, it also reads
`<file name>.<env>.json` from the same folder, on top of the first file.
Environment variables and command-line values override both files. The
current directory is never used.

## 8. Install llama-swap as a login service

launchd (the macOS service manager) starts it at login and restarts it if it
stops. Pick a label, for example your name, and install the service file and
the three helper scripts with it:

```sh
MY_LABEL=your-label   # e.g. your name, lowercase

sed -e "s/your-label/$MY_LABEL/g" -e "s/your-username/$USER/g" \
  scripts/com.your-label.llama-swap.plist \
  > ~/Library/LaunchAgents/com.$MY_LABEL.llama-swap.plist

for s in minerva-llama-install minerva-llama-status minerva-llama-restart; do
  sed "s/your-label/$MY_LABEL/g" scripts/$s > ~/bin/$s
  chmod +x ~/bin/$s
done

minerva-llama-install
minerva-llama-status
```

`minerva-llama-status` prints the service state (`state = running`) and the
models llama-swap knows. No model is loaded until the first request.

| Script | When |
| --- | --- |
| `minerva-llama-install` | after editing the service file (the plist) |
| `minerva-llama-restart` | after editing `llama-swap.yml`; llama-swap reads it only at startup |
| `minerva-llama-status` | to check that the service runs |

The llama-swap log is `~/.local/state/minerva/logs/llama-swap.log`; the
model servers' output is at `http://localhost:9930/logs`.

## 9. Index your notes

Describe the collection in an overlay, for example
`~/.config/minerva/minerva-markdown-indexer.my-notes.json`:

```json
{
  "Indexer": {
    "RootPath": "/path/to/your/vault",
    "CollectionName": "my-notes",
    "Description": "Personal notes: projects, reading notes, journal. Use for questions about my own work and ideas."
  }
}
```

The `Description` is what MCP clients read to decide which collection to
search, so describe the content, not the tool. It is stored when the
collection is created.

Wrap the command in a script, e.g. `~/bin/reindex-my-notes`:

```sh
#!/usr/bin/env bash
set -euo pipefail
DOTNET_ENVIRONMENT=my-notes exec ~/bin/minerva-markdown-indexer "$@"
```

`chmod +x ~/bin/reindex-my-notes`, then run it. The first run embeds every
note and takes a while; later runs process only changed files.

## 10. Smoke test

```sh
minerva-search "a phrase you know is in your notes" --collection my-notes
```

The first query is slow: llama-swap loads the three models.

## 11. Connect the MCP clients

The MCP server finds its config by itself, so the client needs only the
command. Use the full path: clients do not read your shell `PATH`.

Claude Desktop: edit
`~/Library/Application Support/Claude/claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "minerva": {
      "command": "/Users/your-username/bin/minerva-mcp"
    }
  }
}
```

LM Studio: add the same `mcpServers` entry in its built-in MCP config editor.

Restart the client. Ask it something your notes answer; it should call the
`search` tool on your collection.
