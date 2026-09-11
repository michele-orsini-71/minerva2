# SQL scripts

A small collection of PostgreSQL snippets used during development of Minerva.
Most of these are not part of the application's runtime — Minerva creates and
manages its own tables. These scripts cover three things:

1. **Bootstrap** — getting a fresh PostgreSQL instance ready to host Minerva.
2. **Diagnostics** — checking what's installed and what's happening.
3. **Analytics** — inspecting storage and prefix overhead in the `chunks` table.

Open them in DBeaver, psql, or any SQL client. Placeholders like `<password>`
and `<collection>` must be replaced before running.

## Bootstrap (run once, in this order)

Connect as a PostgreSQL superuser (e.g. `postgres`) and run:

1. [create-minerva-role.sql](create-minerva-role.sql) — create the `minerva`
   login role. Replace `<password>`.
2. [create-database-minerva.sql](create-database-minerva.sql) — create the
   `minerva` database owned by that role.
3. Reconnect to the `minerva` database, then:
4. [create-vector-extension.sql](create-vector-extension.sql) — install the
   `pgvector` extension.
5. [verify-vector-extension.sql](verify-vector-extension.sql) — confirm `vector`
   is installed.
6. [create-pgsearch-extension.sql](create-pgsearch-extension.sql) — install the
   `pg_search` extension. Unlike `pgvector`, `pg_search` is not a trusted
   extension: this step must run as a superuser (the `minerva` role cannot),
   and the extension binary must already be installed on the server with
   `pg_search` listed in `shared_preload_libraries` — see
   [installation.md](../../../docs/reference/installation.md).
7. [verify-pgsearch-extension.sql](verify-pgsearch-extension.sql) — confirm
   `pg_search` is installed.

If step 4 or 5 fail, run
[find-available-extensions.sql](find-available-extensions.sql) to check whether
`pgvector` or `pg_search` are available on the server at all.

## Diagnostics

- [monitor-advisory-locks.sql](monitor-advisory-locks.sql) — inspect advisory
  locks held by the application. The second query joins `pg_stat_activity` to
  show which session/query holds each lock — useful when something looks stuck.
- [bm25-smoke-test.sql](bm25-smoke-test.sql) — run a BM25 query directly
  against the `chunks` table (`|||` operator + `pdb.score`). Confirms the
  index answers queries after a migration or re-ingest.

## Analytics on the `chunks` table

- [Per-collection size and column
  breakdown.sql](Per-collection%20size%20and%20column%20breakdown.sql) — total
  bytes per column (content, embedding) grouped by
  collection. The BM25 index is table-wide, not a column; measure it via
  `pg_stat_user_indexes` (see
  [storage-footprint.md](../../../docs/measurements/storage-footprint.md)).

## Maintenance

- [delete-collection.sql](delete-collection.sql) — remove a collection by name.
  Replace `<collection>`. Destructive — double-check the name first.

## Sample usages

```sh
~ % psql -h localhost -U username -d minerva -c "SELECT * from collections"
  name   | description | metadata |          created_at           |        last_updated_at        
---------+-------------+----------+-------------------------------+-------------------------------
 test-1  |             |          | 2026-05-03 19:51:05.499802+02 | 2026-05-03 19:51:05.499802+02
 test-2  |             |          | 2026-05-07 12:08:33.731655+02 | 2026-05-07 12:08:33.731655+02
 qwen2-5 |             |          | 2026-05-10 10:14:47.896628+02 | 2026-05-10 10:14:47.896628+02
(3 rows)

~ % psql -h localhost -U username -d minerva -f "delete-collection.sql" -v name=test-2 

~ % psql -h localhost -U username -d minerva                               
psql (18.4 (Homebrew), server 18.3 (Homebrew))
Type "help" for help.

minerva=# SELECT * from collections;
  name   | description | metadata |          created_at           |        last_updated_at        
---------+-------------+----------+-------------------------------+-------------------------------
 test-1  |             |          | 2026-05-03 19:51:05.499802+02 | 2026-05-03 19:51:05.499802+02
 test-2  |             |          | 2026-05-07 12:08:33.731655+02 | 2026-05-07 12:08:33.731655+02
 qwen2-5 |             |          | 2026-05-10 10:14:47.896628+02 | 2026-05-10 10:14:47.896628+02
(3 rows)

minerva=# quit
~ % 
```
