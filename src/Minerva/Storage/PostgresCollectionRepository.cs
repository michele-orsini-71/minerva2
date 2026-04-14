using System.Text.Json;
using Minerva.Models;
using Npgsql;
using NpgsqlTypes;

namespace Minerva.Storage;

public class PostgresCollectionRepository : ICollectionRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresCollectionRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<Collection?> GetAsync(string name, CancellationToken ct = default)
    {
        const string sql = """
            SELECT name, description, embedding_model, embedding_dimension,
                   metadata, created_at, last_updated_at
            FROM collections WHERE name = @name
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadCollection(reader) : null;
    }

    public async Task<IReadOnlyList<Collection>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT name, description, embedding_model, embedding_dimension,
                   metadata, created_at, last_updated_at
            FROM collections ORDER BY name
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<Collection>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadCollection(reader));
        }
        return results;
    }

    public async Task CreateAsync(Collection collection, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO collections (name, description, embedding_model, embedding_dimension, metadata)
            VALUES (@name, @description, @embedding_model, @embedding_dimension, @metadata)
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", collection.Name);
        cmd.Parameters.AddWithValue("description", (object?)collection.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("embedding_model", collection.EmbeddingModel);
        cmd.Parameters.AddWithValue("embedding_dimension", collection.EmbeddingDimension);
        AddJsonbParameter(cmd, "metadata", collection.Metadata);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE collections
            SET description = @description,
                embedding_model = @embedding_model,
                embedding_dimension = @embedding_dimension,
                metadata = @metadata,
                last_updated_at = NOW()
            WHERE name = @name
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", collection.Name);
        cmd.Parameters.AddWithValue("description", (object?)collection.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("embedding_model", collection.EmbeddingModel);
        cmd.Parameters.AddWithValue("embedding_dimension", collection.EmbeddingDimension);
        AddJsonbParameter(cmd, "metadata", collection.Metadata);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM collections WHERE name = @name";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Collection ReadCollection(NpgsqlDataReader reader)
    {
        var metadataJson = reader.IsDBNull(4) ? null : reader.GetString(4);
        var metadata = metadataJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson)
            : null;

        return new Collection(
            Name: reader.GetString(0),
            Description: reader.IsDBNull(1) ? null : reader.GetString(1),
            EmbeddingModel: reader.GetString(2),
            EmbeddingDimension: reader.GetInt32(3),
            Metadata: metadata,
            CreatedAt: reader.GetFieldValue<DateTimeOffset>(5),
            LastUpdatedAt: reader.GetFieldValue<DateTimeOffset>(6));
    }

    private static void AddJsonbParameter(NpgsqlCommand cmd, string name,
        Dictionary<string, object>? value)
    {
        var param = new NpgsqlParameter(name, NpgsqlDbType.Jsonb);
        param.Value = value is not null ? JsonSerializer.Serialize(value) : DBNull.Value;
        cmd.Parameters.Add(param);
    }
}
