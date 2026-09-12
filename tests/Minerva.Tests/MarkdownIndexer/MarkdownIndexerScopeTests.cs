using Microsoft.Extensions.Logging.Abstractions;
using Minerva.MarkdownIndexer;
using Minerva.Models;
using Minerva.Tests.TestSupport;

// The class MarkdownIndexer sits inside the namespace Minerva.MarkdownIndexer, so the bare name
// resolves to the namespace. Alias the type to refer to it unambiguously.
using MdIndexer = Minerva.MarkdownIndexer.MarkdownIndexer;

namespace Minerva.Tests.Indexer;

[Trait("Category", "MarkdownIndexer")]
public class MarkdownIndexerScopeTests
{
    private const string Root = "/vault";

    // Mirrors what a previous run persisted and what the JSON converter returns on read:
    // arrays come back as a list of scalar objects, not as string[].
    private static Dictionary<string, object> StoredBag(
        string root, string[] excludeDirectories, string[] fileExtensions) =>
        new()
        {
            [MdIndexer.RootPathField] = root,
            [MdIndexer.ExcludeDirectoriesField] = new List<object>(excludeDirectories),
            [MdIndexer.FileExtensionsField] = new List<object>(fileExtensions),
        };

    private static Collection Existing(
        Dictionary<string, object> bag, string kind = "minerva-markdown-indexer") =>
        new("test", null, TestOptions.Provenance(), new ClientProvenance(kind, bag));

    private static IndexerOptions Options(
        string root = Root,
        string[]? excludeDirectories = null,
        string[]? fileExtensions = null,
        bool allowSourceScopeChange = false) =>
        TestOptions.Indexer(
            rootPath: root,
            excludeDirectories: excludeDirectories ?? [".obsidian", ".git"],
            fileExtensions: fileExtensions ?? ["md"],
            allowSourceScopeChange: allowSourceScopeChange);

    private static (MdIndexer indexer, FakeIngestEngine engine) Build(
        IndexerOptions options, Collection? existing)
    {
        var engine = new FakeIngestEngine(existing);
        var scanner = new MarkdownScanner(options);
        var indexer = new MdIndexer(
            scanner, engine, options, NullLogger<MdIndexer>.Instance);
        return (indexer, engine);
    }

    [Fact]
    public async Task NoExistingCollection_WritesScopeBag_AndIngests()
    {
        var options = Options(excludeDirectories: [".obsidian"], fileExtensions: ["md", "txt"]);
        var (indexer, engine) = Build(options, existing: null);

        await indexer.RunAsync();

        Assert.True(engine.Ingested);
        var bag = engine.IngestedProvenance!.data;
        Assert.Equal(Root, bag[MdIndexer.RootPathField]);
        Assert.True(bag.ContainsKey(MdIndexer.ExcludeDirectoriesField));
        Assert.True(bag.ContainsKey(MdIndexer.FileExtensionsField));
    }

    [Fact]
    public async Task MatchingScope_Ingests()
    {
        var options = Options();
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"])));

        await indexer.RunAsync();

        Assert.True(engine.Ingested);
    }

    [Fact]
    public async Task ExcludeDirectories_ReorderedAndDifferentCase_IsNotAChange()
    {
        var options = Options(excludeDirectories: [".obsidian", ".git"]);
        // Stored in different order and case: same set of directories, so no change.
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".GIT", ".Obsidian"], ["md"])));

        await indexer.RunAsync();

        Assert.True(engine.Ingested);
    }

    [Fact]
    public async Task FileExtensions_DifferentDotAndCase_IsNotAChange()
    {
        var options = Options(fileExtensions: [".MD"]);
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"])));

        await indexer.RunAsync();

        Assert.True(engine.Ingested);
    }

    [Fact]
    public async Task RootPathChanged_Throws_AndDoesNotIngest()
    {
        var options = Options(root: "/new-vault");
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"])));

        var ex = await Assert.ThrowsAsync<CollectionScopeChangeNotAllowedException>(
            () => indexer.RunAsync());

        Assert.Contains(MdIndexer.RootPathField, ex.ChangedFields);
        Assert.False(engine.Ingested);
    }

    [Fact]
    public async Task RootPathCaseDiffers_Throws()
    {
        // Root path is compared exactly (case-sensitive) by design.
        var options = Options(root: "/Vault");
        var (indexer, _) = Build(
            options, Existing(StoredBag("/vault", [".obsidian", ".git"], ["md"])));

        await Assert.ThrowsAsync<CollectionScopeChangeNotAllowedException>(
            () => indexer.RunAsync());
    }

    [Fact]
    public async Task ExcludeDirectoriesChanged_Throws()
    {
        var options = Options(excludeDirectories: [".obsidian", ".git", ".trash"]);
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"])));

        var ex = await Assert.ThrowsAsync<CollectionScopeChangeNotAllowedException>(
            () => indexer.RunAsync());

        Assert.Contains(MdIndexer.ExcludeDirectoriesField, ex.ChangedFields);
        Assert.False(engine.Ingested);
    }

    [Fact]
    public async Task FileExtensionsChanged_Throws()
    {
        var options = Options(fileExtensions: ["md", "txt"]);
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"])));

        var ex = await Assert.ThrowsAsync<CollectionScopeChangeNotAllowedException>(
            () => indexer.RunAsync());

        Assert.Contains(MdIndexer.FileExtensionsField, ex.ChangedFields);
        Assert.False(engine.Ingested);
    }

    [Fact]
    public async Task MultipleScopeChanges_AreAllReported()
    {
        var options = Options(root: "/new", excludeDirectories: [".obsidian"], fileExtensions: ["txt"]);
        var (indexer, _) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"])));

        var ex = await Assert.ThrowsAsync<CollectionScopeChangeNotAllowedException>(
            () => indexer.RunAsync());

        Assert.Contains(MdIndexer.RootPathField, ex.ChangedFields);
        Assert.Contains(MdIndexer.ExcludeDirectoriesField, ex.ChangedFields);
        Assert.Contains(MdIndexer.FileExtensionsField, ex.ChangedFields);
    }

    [Fact]
    public async Task ScopeChanged_WithAllowFlag_Ingests()
    {
        var options = Options(root: "/new-vault", allowSourceScopeChange: true);
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"])));

        await indexer.RunAsync();

        Assert.True(engine.Ingested);
    }

    [Fact]
    public async Task ExistingCollectionFromAnotherTool_Throws()
    {
        var options = Options();
        var (indexer, engine) = Build(
            options, Existing(StoredBag(Root, [".obsidian", ".git"], ["md"]), kind: "other-tool"));

        await Assert.ThrowsAsync<NotAnIndexerCollectionException>(() => indexer.RunAsync());
        Assert.False(engine.Ingested);
    }

    [Fact]
    public async Task ProvenanceMissingScopeField_Throws()
    {
        var options = Options();
        var incomplete = new Dictionary<string, object>
        {
            [MdIndexer.RootPathField] = Root,
            [MdIndexer.ExcludeDirectoriesField] = new List<object> { ".obsidian", ".git" },
            // file-extensions deliberately absent
        };
        var (indexer, engine) = Build(options, Existing(incomplete));

        await Assert.ThrowsAsync<ProvenanceMalformedException>(() => indexer.RunAsync());
        Assert.False(engine.Ingested);
    }

    private sealed class FakeIngestEngine(Collection? existing) : IIngestEngine
    {
        public bool Ingested { get; private set; }
        public ClientProvenance? IngestedProvenance { get; private set; }

        public Task<Collection?> QueryCollectionInfoAsync(
            string collectionName, CancellationToken ct = default) =>
            Task.FromResult(existing);

        public Task<IngestionResult> IngestAsync(
            string collectionName,
            ClientProvenance clientProvenance,
            IAsyncEnumerable<Document> documents,
            bool allowRecreateOnConfigMismatch = false,
            CancellationToken ct = default)
        {
            Ingested = true;
            IngestedProvenance = clientProvenance;
            return Task.FromResult(new IngestionResult(0, 0, 0, 0, TimeSpan.Zero, 0));
        }
    }
}
