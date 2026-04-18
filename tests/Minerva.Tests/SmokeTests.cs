using Minerva.Configuration;
using Minerva.Exceptions;
using Minerva.Models;
using Minerva.Utilities;

namespace Minerva.Tests;

[Collection("EnvVars")]
public class SmokeTests
{
    [Fact]
    public void Document_record_properties_are_set()
    {
        var doc = new Document("src-1", "Test Doc", "Hello world");

        Assert.Equal("src-1", doc.SourceId);
        Assert.Equal("Test Doc", doc.Title);
        Assert.Equal("Hello world", doc.Text);
        Assert.Null(doc.Metadata);
        Assert.Null(doc.Attachments);
    }

    [Fact]
    public void IngestionResult_record_properties_are_set()
    {
        var result = new IngestionResult(3, 1, 2, 5, TimeSpan.FromSeconds(1.5));

        Assert.Equal(3, result.Added);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(5, result.Unchanged);
        Assert.Equal(TimeSpan.FromSeconds(1.5), result.Elapsed);
    }

    [Fact]
    public void HashHelper_GenerateChunkId_is_deterministic()
    {
        var id1 = HashHelper.GenerateChunkId("doc1", 0);
        var id2 = HashHelper.GenerateChunkId("doc1", 0);

        Assert.Equal(id1, id2);
        Assert.Equal(64, id1.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void HashHelper_GenerateChunkId_varies_by_index()
    {
        var id0 = HashHelper.GenerateChunkId("doc1", 0);
        var id1 = HashHelper.GenerateChunkId("doc1", 1);

        Assert.NotEqual(id0, id1);
    }

    [Fact]
    public void HashHelper_ComputeContentHash_is_deterministic()
    {
        var hash1 = HashHelper.ComputeContentHash("some content");
        var hash2 = HashHelper.ComputeContentHash("some content");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashHelper_ComputeContentHash_differs_for_different_content()
    {
        var hash1 = HashHelper.ComputeContentHash("content A");
        var hash2 = HashHelper.ComputeContentHash("content B");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void CredentialResolver_resolves_env_var()
    {
        Environment.SetEnvironmentVariable("MINERVA_TEST_KEY", "resolved-value");
        try
        {
            var result = CredentialResolver.Resolve("${MINERVA_TEST_KEY}");
            Assert.Equal("resolved-value", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINERVA_TEST_KEY", null);
        }
    }

    [Fact]
    public void CredentialResolver_rejects_literal_api_key()
    {
        Assert.Throws<ConfigurationException>(() => CredentialResolver.Resolve("sk-abc123"));
    }

    [Fact]
    public void CredentialResolver_throws_for_missing_env_var()
    {
        Environment.SetEnvironmentVariable("MINERVA_NONEXISTENT", null);

        Assert.Throws<ConfigurationException>(() => CredentialResolver.Resolve("${MINERVA_NONEXISTENT}"));
    }

    [Fact]
    public void CredentialResolver_passes_through_plain_values()
    {
        var result = CredentialResolver.Resolve("http://localhost:11434");
        Assert.Equal("http://localhost:11434", result);
    }
}
