# Installation requirements

Minimal list of what a machine needs before Minerva runs. Current dev setup
is macOS + Homebrew; adjust paths for other platforms.

## Runtime

- **.NET 10 SDK**
- **PostgreSQL 18** (`brew install postgresql@18`, run as a service)

## PostgreSQL extensions

| Extension | Install | `CREATE EXTENSION` |
| --- | --- | --- |
| `pgvector` | `brew install pgvector` | automatic (migration 001; trusted extension) |
| `pg_search` | prebuilt pkg, see below | **manual, superuser** (migration 004 only no-ops past it) |

`pg_search` (ParadeDB, the BM25 leg) sequence:

1. Download the pkg matching the Postgres major version and macOS codename
   from [ParadeDB releases](https://github.com/paradedb/paradedb/releases)
   (e.g. `pg_search@18--<version>.arm64_tahoe.pkg`) and run
   `sudo installer -pkg <file> -target /`.
2. Add `pg_search` to `shared_preload_libraries` in `postgresql.conf`
   (`$(brew --prefix)/var/postgresql@18/postgresql.conf`; the setting is one
   comma-separated list).
3. `brew services restart postgresql@18`.
4. As a superuser (the app's `minerva` role cannot — `pg_search` is not a
   trusted extension), run
   `src/Minerva/sql-scripts/create-pgsearch-extension.sql` against the
   `minerva` database, and verify with
   `verify-pgsearch-extension.sql`.

Note: since pg_search v0.25 `pgvector` must be installed before it.

Database and role bootstrap (once): see the Bootstrap section of
[sql-scripts/README.md](../../src/Minerva/sql-scripts/README.md).

## Model servers

- **Embeddings + contextualizer LLM**: an OpenAI-compatible server on
  `127.0.0.1:1234` (LM Studio in dev) serving `text-embedding-bge-m3` and the
  contextualization model. Health-check curl commands in
  [running-and-debugging.md](running-and-debugging.md).
- **Reranker** (optional leg): `bge-reranker-v2-m3` via a llama.cpp server;
  only needed when `EnableReranker` is on.
