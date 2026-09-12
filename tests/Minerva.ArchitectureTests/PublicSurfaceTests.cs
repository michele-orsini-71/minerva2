using System.Reflection;

namespace Minerva.ArchitectureTests;

// The library's public surface is the two engines with their builders, plus the
// models, options and exceptions they exchange with callers. Everything else
// (pipelines, repositories, providers, helpers) is internal; tests and tools that
// need it are granted InternalsVisibleTo in Minerva.csproj.
public class PublicSurfaceTests
{
    private static readonly Assembly Minerva = typeof(ISearchEngine).Assembly;

    private static readonly IReadOnlySet<string> PublicNamespaces = new HashSet<string>
    {
        "Minerva",
        "Minerva.Models",
        "Minerva.Configuration",
        "Minerva.Exceptions",
    };

    [Fact]
    public void PublicTypes_LiveOnlyInPublicNamespaces()
    {
        var leaked = Minerva.GetTypes()
            .Where(t => t.IsPublic && t.Namespace is not null)
            .Where(t => !PublicNamespaces.Contains(t.Namespace!))
            .Select(t => t.FullName)
            .OrderBy(n => n)
            .ToList();

        if (leaked.Count > 0)
        {
            Assert.Fail(
                "Public type(s) found outside the library's public surface:\n  - "
                + string.Join("\n  - ", leaked)
                + "\nMake them internal, or move them to one of: "
                + string.Join(", ", PublicNamespaces) + ".");
        }
    }
}
