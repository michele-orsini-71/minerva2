using Minerva.Search.Bench.Sweep;

namespace Minerva.Tests.Search.Bench;


[Trait("Category", "Bench")]
public class OutputDirectoryTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 6, 5, 14, 30, 22, TimeSpan.Zero);

    [Fact]
    public void Mint_CreatesLeaf_WithTimestampAndSlug()
    {
        var parent = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var leaf = OutputDirectory.Mint(parent, Timestamp, "eval/datasets/private/personal-notes-v1.jsonl");

            Assert.Equal(
                Path.Combine(parent, "2026-06-05T14-30-22Z_personal-notes-v1"),
                leaf);
            Assert.True(Directory.Exists(leaf));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void Mint_WhenLeafExists_Throws()
    {
        var parent = Directory.CreateTempSubdirectory().FullName;
        try
        {
            OutputDirectory.Mint(parent, Timestamp, "personal-notes-v1.jsonl");

            var ex = Assert.Throws<IOException>(
                () => OutputDirectory.Mint(parent, Timestamp, "personal-notes-v1.jsonl"));
            Assert.Contains("already exists", ex.Message);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
