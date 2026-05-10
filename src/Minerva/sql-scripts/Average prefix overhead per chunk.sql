SELECT
    collection_name,
    AVG(pg_column_size(contextual_prefix))::int AS avg_prefix_bytes,
    AVG(pg_column_size(content))::int           AS avg_content_bytes,
    ROUND(100.0 * AVG(pg_column_size(contextual_prefix))
                / NULLIF(AVG(pg_column_size(content)), 0), 1) AS prefix_pct_of_content
FROM chunks
GROUP BY collection_name;
