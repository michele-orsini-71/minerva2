-- Workaround for a pg_search 0.25.5/0.25.6 bug, observed 2026-09-07.
--
-- Minerva's full-text query combines a plain filter (collection_name = ...) with a
-- BM25 match. pg_search 0.25.5 added "bitmap intersection" for exactly this shape:
-- it intersects the Postgres bitmap scan of the filter with its own scan. For some
-- query/segment combinations that path fails deterministically with
--   ERROR: bitmap intersection stream (consumer 0, segment <id>) claimed twice
-- Disabling the feature makes the planner apply the filter inside the ParadeDB scan
-- instead. Measured cost: roughly 2x on a 20 ms query. Applied at database level so
-- every connection (indexer, bench, search CLI) gets it without code changes.
--
-- Run as the database owner or a superuser. Reconnect afterwards: the setting
-- applies to new sessions only.
ALTER DATABASE minerva SET paradedb.enable_bitmap_intersection = off;

-- Verify from a fresh session:
--   SHOW paradedb.enable_bitmap_intersection;   -- expected: off
--
-- Once a pg_search release fixes the bug, restore the default with:
--   ALTER DATABASE minerva RESET paradedb.enable_bitmap_intersection;
