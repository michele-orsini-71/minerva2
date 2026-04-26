using System.Reflection;

namespace Minerva.ArchitectureTests;

// Defense against architectural drift: if someone introduces a new top-level
// Minerva.* namespace, this test forces them to classify it into a layer
// (and add matching rules in LayerDependencyTests) before the build passes.
public class NamespaceCoverageTests
{
    private static readonly Assembly Minerva = typeof(MinervaEngine).Assembly;

    // Every top-level Minerva.* namespace must appear here, paired with its
    // Clean Architecture layer. Sub-namespaces (e.g. Minerva.Models.Foo) are
    // treated as belonging to their parent top-level namespace.
    private static readonly IReadOnlyDictionary<string, string> ClassifiedNamespaces =
        new Dictionary<string, string>
        {
            ["Minerva"] = "USE_CASES",               // MinervaEngine, IMinervaEngine
            ["Minerva.Models"] = "ENTITIES",
            ["Minerva.Exceptions"] = "ENTITIES",
            ["Minerva.Utilities"] = "ENTITIES",
            ["Minerva.Collections"] = "USE_CASES",
            ["Minerva.Ingestion"] = "USE_CASES",
            ["Minerva.Search"] = "USE_CASES",
            ["Minerva.Readiness"] = "USE_CASES",
            ["Minerva.Storage"] = "ADAPTERS",
            ["Minerva.Providers"] = "ADAPTERS",
            ["Minerva.Configuration"] = "FRAMEWORK",
            ["Minerva.DI"] = "FRAMEWORK",
        };

    [Fact]
    public void EveryTopLevelMinervaNamespace_IsClassifiedIntoALayer()
    {
        var discovered = Minerva.GetTypes()
            .Where(t => !t.IsNested && t.Namespace is not null)
            .Select(t => TopLevel(t.Namespace!))
            .Where(ns => ns.StartsWith("Minerva"))
            .Distinct()
            .OrderBy(ns => ns)
            .ToList();

        var unclassified = discovered
            .Where(ns => !ClassifiedNamespaces.ContainsKey(ns))
            .ToList();

        if (unclassified.Count > 0)
        {
            Assert.Fail(
                "Found top-level Minerva.* namespace(s) not classified into a Clean Architecture layer:\n  - "
                + string.Join("\n  - ", unclassified)
                + "\nAdd each one to NamespaceCoverageTests.ClassifiedNamespaces AND add the matching "
                + "dependency rules to LayerDependencyTests. See docs/architecture.md for the layer model.");
        }

        var stale = ClassifiedNamespaces.Keys
            .Where(ns => !discovered.Contains(ns))
            .ToList();

        if (stale.Count > 0)
        {
            Assert.Fail(
                "ClassifiedNamespaces lists namespace(s) that no longer exist in the Minerva assembly:\n  - "
                + string.Join("\n  - ", stale)
                + "\nRemove the stale entries (and any matching rules in LayerDependencyTests).");
        }
    }

    private static string TopLevel(string ns)
    {
        // "Minerva"           -> "Minerva"
        // "Minerva.Models"    -> "Minerva.Models"
        // "Minerva.Models.X"  -> "Minerva.Models"
        var parts = ns.Split('.', 3);
        return parts.Length switch
        {
            1 => parts[0],
            _ => $"{parts[0]}.{parts[1]}",
        };
    }
}
