using System.Text.Json;
using System.Text.Json.Serialization;
using Minerva.Collections;
using Minerva.Models;
using Npgsql;
using NpgsqlTypes;

namespace Minerva.Storage;

public class PostgresCollectionRepository : ICollectionRepository
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly NpgsqlDataSource _dataSource;

    public PostgresCollectionRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<Collection?> GetAsync(string name, CancellationToken ct = default)
    {
        const string sql = """
            SELECT name, description, metadata, created_at, last_updated_at
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
            SELECT name, description, metadata, created_at, last_updated_at
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
            INSERT INTO collections (name, description, metadata)
            VALUES (@name, @description, @metadata)
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", collection.Name);
        cmd.Parameters.AddWithValue("description", (object?)collection.Description ?? DBNull.Value);
        AddMetadataParameter(cmd, "metadata", collection);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE collections
            SET description = @description,
                metadata = @metadata,
                last_updated_at = NOW()
            WHERE name = @name
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", collection.Name);
        cmd.Parameters.AddWithValue("description", (object?)collection.Description ?? DBNull.Value);
        AddMetadataParameter(cmd, "metadata", collection);

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
        try
        {
            var metadataJson = reader.GetString(2);
            var bag = JsonSerializer.Deserialize<MetadataBag>(metadataJson, MetadataJsonOptions)
                ?? throw new InvalidOperationException(
                    $"Collection '{reader.GetString(0)}' has unreadable metadata.");

            if (bag.Client is null || bag.Provenance is null)
            {
                throw new InvalidOperationException(
                    $"Collection '{reader.GetString(0)}' has incomplete metadata; recreate it");
            }

            return new Collection(
                Name: reader.GetString(0),
                Description: reader.IsDBNull(1) ? null : reader.GetString(1),
                Provenance: bag.Provenance,
                ClientProvenance: bag.Client,
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(3),
                LastUpdatedAt: reader.GetFieldValue<DateTimeOffset>(4));
        }
        catch (Exception ex) when (ex is InvalidCastException or ArgumentNullException or JsonException)
        {
            throw new InvalidOperationException(
                $"Collection '{reader.GetString(0)}' has malformed or unreadable metadata.", ex);
        }
    }

    private static void AddMetadataParameter(NpgsqlCommand cmd, string name, Collection collection)
    {
        var bag = new MetadataBag(collection.Provenance, collection.ClientProvenance);
        var param = new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(bag, MetadataJsonOptions),
        };
        cmd.Parameters.Add(param);
    }

    private sealed record MetadataBag(
        CollectionProvenance Provenance,
        ClientProvenance Client);
}
