using Minerva.Models;
using Minerva.Storage;

namespace Minerva.IntegrationTests.Storage;

[Collection("Storage")]
[Trait("Category", "Storage")]
public class CollectionRepositoryTests : IAsyncLifetime
{
    private readonly StorageTestFixture _fixture;
    private readonly PostgresCollectionRepository _repo;

    public CollectionRepositoryTests(StorageTestFixture fixture)
    {
        _fixture = fixture;
        _repo = new PostgresCollectionRepository(fixture.DataSource);
    }

    public async Task InitializeAsync() => await _fixture.CleanupAsync(honorDisableFlag: false);
    public async Task DisposeAsync() => await _fixture.CleanupAsync();

    [Fact]
    public async Task CreateAndGet_RoundTrips()
    {
        var collection = new Collection("test-coll", "A test collection",
            "text-embedding-3-small", 1536);

        await _repo.CreateAsync(collection);
        var retrieved = await _repo.GetAsync("test-coll");

        Assert.NotNull(retrieved);
        Assert.Equal("test-coll", retrieved.Name);
        Assert.Equal("A test collection", retrieved.Description);
        Assert.Equal("text-embedding-3-small", retrieved.EmbeddingModel);
        Assert.Equal(1536, retrieved.EmbeddingDimension);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenNotFound()
    {
        var result = await _repo.GetAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task List_ReturnsAllCollections()
    {
        await _repo.CreateAsync(new Collection("coll-a", null, "model-a", 768));
        await _repo.CreateAsync(new Collection("coll-b", null, "model-b", 1536));

        var list = await _repo.ListAsync();

        Assert.True(list.Count >= 2);
        Assert.Contains(list, c => c.Name == "coll-a");
        Assert.Contains(list, c => c.Name == "coll-b");
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        await _repo.CreateAsync(new Collection("update-coll", "old desc", "model", 768));

        var updated = new Collection("update-coll", "new desc", "model-v2", 1024);
        await _repo.UpdateAsync(updated);

        var retrieved = await _repo.GetAsync("update-coll");
        Assert.NotNull(retrieved);
        Assert.Equal("new desc", retrieved.Description);
        Assert.Equal("model-v2", retrieved.EmbeddingModel);
        Assert.Equal(1024, retrieved.EmbeddingDimension);
    }

    [Fact]
    public async Task Delete_RemovesCollection()
    {
        await _repo.CreateAsync(new Collection("delete-coll", null, "model", 768));
        await _repo.DeleteAsync("delete-coll");

        var result = await _repo.GetAsync("delete-coll");
        Assert.Null(result);
    }

    [Fact]
    public async Task Metadata_RoundTrips_AsJsonb()
    {
        var metadata = new Dictionary<string, object>
        {
            ["source"] = "test",
            ["version"] = 2
        };
        var collection = new Collection("meta-coll", null, "model", 768, Metadata: metadata);

        await _repo.CreateAsync(collection);
        var retrieved = await _repo.GetAsync("meta-coll");

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Metadata);
        Assert.Equal("test", retrieved.Metadata["source"].ToString());
    }
}
