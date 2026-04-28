using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minerva.Configuration;
using Minerva.Ingestion;
using Minerva.Readiness;
using Npgsql;

namespace Minerva.MarkdownWatcher.Readiness;

internal interface ICollectionDimensionProbe
{
    Task<int?> TryGetStoredDimensionAsync(string collectionName, CancellationToken ct);
}

public sealed class CollectionDimensionMatchCheck : IReadinessCheck, IReadinessCheckTimeout
{
    private readonly MinervaOptions _minervaOptions;
    private readonly WatcherOptions _watcherOptions;
    private readonly IEmbeddingDimensionProvider? _dimensionProvider;
    private readonly ICollectionDimensionProbe? _probe;
    private readonly ILogger<CollectionDimensionMatchCheck> _logger;

    public CollectionDimensionMatchCheck(
        IOptions<MinervaOptions> minervaOptions,
        IOptions<WatcherOptions> watcherOptions,
        IServiceProvider serviceProvider,
        ILogger<CollectionDimensionMatchCheck> logger)
    {
        _minervaOptions = minervaOptions.Value;
        _watcherOptions = watcherOptions.Value;
        _dimensionProvider = serviceProvider.GetService<IEmbeddingDimensionProvider>();
        var ds = serviceProvider.GetService<NpgsqlDataSource>();
        _probe = ds is null ? null : new NpgsqlCollectionDimensionProbe(ds);
        _logger = logger;
    }

    internal CollectionDimensionMatchCheck(
        MinervaOptions minervaOptions,
        WatcherOptions watcherOptions,
        IEmbeddingDimensionProvider? dimensionProvider,
        ICollectionDimensionProbe? probe,
        ILogger<CollectionDimensionMatchCheck> logger)
    {
        _minervaOptions = minervaOptions;
        _watcherOptions = watcherOptions;
        _dimensionProvider = dimensionProvider;
        _probe = probe;
        _logger = logger;
    }

    public string Name => nameof(CollectionDimensionMatchCheck);
    public ReadinessCategory Category => ReadinessCategory.Client;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);

    public async Task<ReadinessCheckResult> RunAsync(CancellationToken ct)
    {
        if (_minervaOptions.ConnectionString is null
            || _minervaOptions.Embedding is null
            || _dimensionProvider is null
            || _probe is null)
        {
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.CLIENT.DIMENSION_NOT_CONFIGURED",
                Message: null,
                Remediation: null);
        }

        // Branch 1: embedder unavailable — already reported by EmbeddingCallCheck; skip silently.
        int probedDim;
        try
        {
            probedDim = await _dimensionProvider.GetDimensionAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping dimension match: embedder unavailable");
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.CLIENT.DIMENSION_SKIPPED",
                Message: "skipped: embedder unavailable",
                Remediation: null);
        }

        var collectionName = _watcherOptions.CollectionName;

        // Branches 2 & 3: probe returns null when the collections table is missing (42P01)
        // OR no row matches the configured name. Both are pass-trivially cases.
        int? storedDim = await _probe.TryGetStoredDimensionAsync(collectionName, ct);
        if (storedDim is null)
        {
            return new ReadinessCheckResult(
                Name, Category, Passed: true,
                Code: "MINERVA.CLIENT.DIMENSION_OK",
                Message: null,
                Remediation: null);
        }

        // Branch 4: stored != probed.
        if (storedDim.Value != probedDim)
        {
            return new ReadinessCheckResult(
                Name, Category, Passed: false,
                Code: "MINERVA.CLIENT.DIMENSION_MISMATCH",
                Message: $"Configured embedder produces {probedDim} dimensions; existing collection '{collectionName}' uses {storedDim.Value}.",
                Remediation: $"Configured embedder produces {probedDim} dimensions; existing collection '{collectionName}' uses {storedDim.Value}. Either revert the embedder change, or drop the collection and its data (DELETE FROM collections WHERE name='{collectionName}') and let it rebuild on next run.");
        }

        return new ReadinessCheckResult(
            Name, Category, Passed: true,
            Code: "MINERVA.CLIENT.DIMENSION_OK",
            Message: null,
            Remediation: null);
    }

    private sealed class NpgsqlCollectionDimensionProbe : ICollectionDimensionProbe
    {
        private readonly NpgsqlDataSource _dataSource;

        public NpgsqlCollectionDimensionProbe(NpgsqlDataSource dataSource) => _dataSource = dataSource;

        public async Task<int?> TryGetStoredDimensionAsync(string collectionName, CancellationToken ct)
        {
            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);
                await using var cmd = new NpgsqlCommand(
                    "SELECT embedding_dimension FROM collections WHERE name = @name", conn);
                cmd.Parameters.AddWithValue("name", collectionName);
                var result = await cmd.ExecuteScalarAsync(ct);
                if (result is null || result is DBNull)
                {
                    return null;
                }
                return Convert.ToInt32(result);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                return null;
            }
        }
    }
}
