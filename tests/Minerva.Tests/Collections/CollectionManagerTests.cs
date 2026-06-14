using Minerva.Collections;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Tests.TestSupport;
using NSubstitute;

namespace Minerva.Tests.Collections;

[Trait("Category", "Collections")]
public class CollectionManagerTests
{
    private const string ValidName = "docs";
    private const string ValidModel = "text-embedding-3-small";
    private const int ValidDimension = 1536;

    private static CollectionProvenance ValidProvenance(
        string model = ValidModel, int dimension = ValidDimension) =>
        TestOptions.Provenance(embeddingModel: model, embeddingDimension: dimension);

    private static CollectionManager CreateManager(
        out ICollectionRepository repo,
        out ICollectionProvisioner provisioner)
    {
        repo = Substitute.For<ICollectionRepository>();
        provisioner = Substitute.For<ICollectionProvisioner>();
        repo.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Collection?)null);
        return new CollectionManager(repo, provisioner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has spaces")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("has_underscore")]
    [InlineData("has.dot")]
    [InlineData("has/slash")]
    [InlineData("'; DROP TABLE chunks;--")]
    public async Task CreateAsync_InvalidName_Throws(string name)
    {
        var mgr = CreateManager(out _, out _);

        await Assert.ThrowsAsync<ConfigurationException>(() =>
            mgr.CreateAsync(name, ValidProvenance()));
    }

    [Theory]
    [InlineData("docs")]
    [InlineData("Docs")]
    [InlineData("docs-v2")]
    [InlineData("a")]
    [InlineData("123")]
    [InlineData("mixed-Case-123")]
    public async Task CreateAsync_ValidName_Succeeds(string name)
    {
        var mgr = CreateManager(out var repo, out var provisioner);
        repo.GetAsync(name, Arg.Any<CancellationToken>())
            .Returns((Collection?)null, new Collection(name, null, ValidProvenance()));

        var result = await mgr.CreateAsync(name, ValidProvenance());

        Assert.Equal(name, result.Name);
        await provisioner.Received(1).EnsureHnswIndexAsync(
            name, ValidDimension, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_EmptyModel_Throws()
    {
        var mgr = CreateManager(out _, out _);

        await Assert.ThrowsAsync<ConfigurationException>(() =>
            mgr.CreateAsync(ValidName, ValidProvenance(model: "")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_NonPositiveDimension_Throws(int dimension)
    {
        var mgr = CreateManager(out _, out _);

        await Assert.ThrowsAsync<ConfigurationException>(() =>
            mgr.CreateAsync(ValidName, ValidProvenance(dimension: dimension)));
    }

    [Fact]
    public async Task CreateAsync_CollectionAlreadyExists_Throws()
    {
        var mgr = CreateManager(out var repo, out var provisioner);
        repo.GetAsync(ValidName, Arg.Any<CancellationToken>())
            .Returns(new Collection(ValidName, null, ValidProvenance()));

        await Assert.ThrowsAsync<ConfigurationException>(() =>
            mgr.CreateAsync(ValidName, ValidProvenance()));

        await provisioner.DidNotReceive().EnsureHnswIndexAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
