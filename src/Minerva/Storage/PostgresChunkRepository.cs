using System.Text.Json;
using Minerva.Ingestion;
using Minerva.Models;
using Minerva.Search;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace Minerva.Storage;

public class PostgresChunkRepository : IChunkWriter, IChunkQuery, IChunkCatalog
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresChunkRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertChunksAsync(string collectionName, string sourceId,
        IReadOnlyList<ChunkWithEmbedding> chunks, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1. Delete existing chunks for this source
        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM chunks WHERE collection_name = @coll AND source_id = @src", conn, tx))
        {
            deleteCmd.Parameters.AddWithValue("coll", collectionName);
            deleteCmd.Parameters.AddWithValue("src", sourceId);
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Insert new chunks (without adjacency pointers first, to avoid FK violations)
        const string insertSql = """
            INSERT INTO chunks (id, collection_name, source_id, chunk_index, content, content_hash,
                               contextual_prefix, embedding, fts_vector, metadata)
            VALUES (@id, @coll, @src, @idx, @content, @hash, @prefix,
                    @embedding, to_tsvector('english', @fts_content), @metadata)
            """;

        foreach (var chunk in chunks)
        {
            await using var cmd = new NpgsqlCommand(insertSql, conn, tx);
            cmd.Parameters.AddWithValue("id", chunk.Id);
            cmd.Parameters.AddWithValue("coll", chunk.CollectionName);
            cmd.Parameters.AddWithValue("src", chunk.SourceId);
            cmd.Parameters.AddWithValue("idx", chunk.ChunkIndex);
            cmd.Parameters.AddWithValue("content", chunk.Content);
            cmd.Parameters.AddWithValue("hash", chunk.ContentHash);
            cmd.Parameters.AddWithValue("prefix", (object?)chunk.ContextualPrefix ?? DBNull.Value);

            cmd.Parameters.AddWithValue("embedding", new Vector(chunk.Embedding));

            // FTS content: the raw content without contextual prefix
            cmd.Parameters.AddWithValue("fts_content", chunk.Content);

            AddJsonbParameter(cmd, "metadata", chunk.Metadata);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3. Update adjacency pointers
        if (chunks.Count > 1)
        {
            const string updatePrevNext = """
                UPDATE chunks SET prev_chunk_id = @prev, next_chunk_id = @next
                WHERE id = @id
                """;

            for (int i = 0; i < chunks.Count; i++)
            {
                await using var cmd = new NpgsqlCommand(updatePrevNext, conn, tx);
                cmd.Parameters.AddWithValue("id", chunks[i].Id);
                cmd.Parameters.AddWithValue("prev",
                    i > 0 ? chunks[i - 1].Id : DBNull.Value);
                cmd.Parameters.AddWithValue("next",
                    i < chunks.Count - 1 ? chunks[i + 1].Id : DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
    }

    public async Task DeleteBySourceIdAsync(string collectionName, string sourceId,
        CancellationToken ct = default)
    {
        const string sql = "DELETE FROM chunks WHERE collection_name = @coll AND source_id = @src";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("coll", collectionName);
        cmd.Parameters.AddWithValue("src", sourceId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetContentHashAsync(string collectionName, string sourceId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT content_hash FROM chunks
            WHERE collection_name = @coll AND source_id = @src AND chunk_index = 0
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("coll", collectionName);
        cmd.Parameters.AddWithValue("src", sourceId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSourceIdsAndHashesAsync(
        string collectionName, CancellationToken ct = default)
    {
        const string sql = """
            SELECT source_id, content_hash FROM chunks
            WHERE collection_name = @coll AND chunk_index = 0
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("coll", collectionName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, string>();
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public async Task<bool> SourceIdExistsAsync(string collectionName, string sourceId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT 1 FROM chunks
            WHERE collection_name = @coll AND source_id = @src
            LIMIT 1
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("coll", collectionName);
        cmd.Parameters.AddWithValue("src", sourceId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    public async Task<IReadOnlyList<ChunkRecord>> GetAdjacentChunksAsync(
        IReadOnlyList<string> chunkIds, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, source_id, collection_name, chunk_index, content, content_hash,
                   contextual_prefix, prev_chunk_id, next_chunk_id, metadata
            FROM chunks WHERE id = ANY(@ids)
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", chunkIds.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ChunkRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadChunkRecord(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<ChunkSearchRecord>> VectorSearchAsync(
        string collectionName, float[] queryEmbedding, int topK,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, source_id, collection_name, chunk_index, content, metadata,
                   prev_chunk_id, next_chunk_id,
                   embedding <=> @embedding::vector AS distance
            FROM chunks
            WHERE collection_name = @coll
            ORDER BY embedding <=> @embedding::vector
            LIMIT @topk
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.AddWithValue("embedding", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("coll", collectionName);
        cmd.Parameters.AddWithValue("topk", topK);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ChunkSearchRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadSearchRecord(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<ChunkSearchRecord>> FullTextSearchAsync(
        string collectionName, string query, int topK,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, source_id, collection_name, chunk_index, content, metadata,
                   prev_chunk_id, next_chunk_id,
                   ts_rank(fts_vector, plainto_tsquery('english', @query)) AS rank
            FROM chunks
            WHERE collection_name = @coll AND fts_vector @@ plainto_tsquery('english', @query)
            ORDER BY rank DESC
            LIMIT @topk
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("query", query);
        cmd.Parameters.AddWithValue("coll", collectionName);
        cmd.Parameters.AddWithValue("topk", topK);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ChunkSearchRecord>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadSearchRecord(reader));
        }
        return results;
    }

    private static ChunkRecord ReadChunkRecord(NpgsqlDataReader reader)
    {
        var metadataJson = reader.IsDBNull(9) ? null : reader.GetString(9);
        var metadata = metadataJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)
            : null;

        return new ChunkRecord(
            Id: reader.GetString(0),
            SourceId: reader.GetString(1),
            CollectionName: reader.GetString(2),
            ChunkIndex: reader.GetInt32(3),
            Content: reader.GetString(4),
            ContentHash: reader.GetString(5),
            ContextualPrefix: reader.IsDBNull(6) ? null : reader.GetString(6),
            PrevChunkId: reader.IsDBNull(7) ? null : reader.GetString(7),
            NextChunkId: reader.IsDBNull(8) ? null : reader.GetString(8),
            Metadata: metadata);
    }

    private static ChunkSearchRecord ReadSearchRecord(NpgsqlDataReader reader)
    {
        var metadataJson = reader.IsDBNull(5) ? null : reader.GetString(5);
        var metadata = metadataJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)
            : null;

        return new ChunkSearchRecord(
            Id: reader.GetString(0),
            SourceId: reader.GetString(1),
            CollectionName: reader.GetString(2),
            ChunkIndex: reader.GetInt32(3),
            Content: reader.GetString(4),
            Score: reader.GetDouble(8),
            PrevChunkId: reader.IsDBNull(6) ? null : reader.GetString(6),
            NextChunkId: reader.IsDBNull(7) ? null : reader.GetString(7),
            Metadata: metadata);
    }

    private static void AddJsonbParameter(NpgsqlCommand cmd, string name,
        Dictionary<string, object>? value)
    {
        var param = new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = value is not null ? JsonSerializer.Serialize(value) : DBNull.Value
        };
        cmd.Parameters.Add(param);
    }
}
