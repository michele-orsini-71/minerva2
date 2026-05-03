using System.Reflection;
using NetArchTest.Rules;

namespace Minerva.ArchitectureTests;

// Clean Architecture layer model (see docs/architecture.md):
//   ENTITIES   : Minerva.Models, Minerva.Exceptions, Minerva.Utilities
//   USE_CASES  : Minerva (root), Minerva.Collections, Minerva.Ingestion, Minerva.Search
//   ADAPTERS   : Minerva.Storage, Minerva.Providers
//   FRAMEWORK  : Minerva.Configuration
//
// Source-code dependencies must point INWARD only (ENTITIES <- USE_CASES <- ADAPTERS <- FRAMEWORK).
public class LayerDependencyTests
{
    private static readonly Assembly Minerva = typeof(IMinervaEngine).Assembly;

    // --- ENTITIES ring ---------------------------------------------------

    [Fact]
    public void Models_HasNoDependencyOnOtherMinervaNamespaces()
    {
        // why: Models is the innermost ring (Entities). It must know nothing about
        // any other layer, inside or outside.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Models")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Collections", "Minerva.Ingestion", "Minerva.Search",
                "Minerva.Storage", "Minerva.Providers",
                "Minerva.Configuration")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Models (Entities ring) must not depend on any other Minerva.* namespace.");
    }

    [Fact]
    public void Exceptions_HasNoDependencyOnOtherMinervaNamespaces()
    {
        // why: Exceptions define core domain vocabulary. Inner ring, no outward deps.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Exceptions")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Collections", "Minerva.Ingestion", "Minerva.Search",
                "Minerva.Storage", "Minerva.Providers",
                "Minerva.Configuration",
                "Minerva.Models", "Minerva.Utilities")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Exceptions (Entities ring) must not depend on any other Minerva.* namespace.");
    }

    [Fact]
    public void Utilities_HasNoDependencyOnOtherMinervaNamespaces()
    {
        // why: Utilities holds pure helpers (e.g. hashing). Inner ring, no outward deps.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Utilities")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Collections", "Minerva.Ingestion", "Minerva.Search",
                "Minerva.Storage", "Minerva.Providers",
                "Minerva.Configuration",
                "Minerva.Models", "Minerva.Exceptions")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Utilities (Entities ring) must not depend on any other Minerva.* namespace.");
    }

    // --- USE_CASES ring --------------------------------------------------

    [Fact]
    public void Collections_DoesNotDependOnAdaptersOrFramework()
    {
        // why: Collections orchestrates entities; it must not know about Storage
        // (Adapters) or Configuration (Framework). Ports it needs should live
        // in the inner ring, not in Storage.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Collections")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Storage", "Minerva.Providers",
                "Minerva.Configuration")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Collections (Use Cases) must not depend on Adapters or Framework rings.");
    }

    [Fact]
    public void Ingestion_DoesNotDependOnAdaptersOrFramework()
    {
        // why: IngestionPipeline should talk to interfaces defined inside Ingestion
        // (or Models), not reach into Storage/Configuration directly.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Ingestion")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Storage", "Minerva.Providers",
                "Minerva.Configuration")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Ingestion (Use Cases) must not depend on Adapters or Framework rings.");
    }

    [Fact]
    public void Search_DoesNotDependOnAdaptersOrFramework()
    {
        // why: Search types (VectorSearch, RankFusion, ContextExpander) must work on
        // domain records, not on Storage DTOs like ChunkSearchRecord.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Search")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Storage", "Minerva.Providers",
                "Minerva.Configuration")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Search (Use Cases) must not depend on Adapters or Framework rings.");
    }

    // --- ADAPTERS ring ---------------------------------------------------

    [Fact]
    public void Providers_DoesNotDependOnFramework()
    {
        // why: Providers is an Adapter ring. Configuration is Framework ring;
        // provider factories should accept primitive parameters or a domain
        // options record, not read framework config DTOs directly.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Providers")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Configuration")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Providers (Adapters) must not depend on the Framework ring.");
    }

    [Fact]
    public void Storage_DoesNotDependOnFramework()
    {
        // why: Storage adapters should be usable independent of how the host app
        // wires configuration.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespace("Minerva.Storage")
            .Should().NotHaveDependencyOnAny(
                "Minerva.Configuration")
            .GetResult();

        ArchAssert.Passes(result, "Minerva.Storage (Adapters) must not depend on the Framework ring.");
    }

    // --- External framework leakage into inner rings ---------------------

    [Fact]
    public void UseCases_DoNotDependOnMicrosoftExtensionsAI()
    {
        // why: Microsoft.Extensions.AI is a framework-ring abstraction (IChatClient,
        // IEmbeddingGenerator, ChatMessage, ChatRole). Use cases must talk to
        // Minerva-defined ports; provider adapters should wrap the MS.AI types.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespaceMatching(@"^Minerva(\.Collections|\.Ingestion|\.Search)?$")
            .Should().NotHaveDependencyOn("Microsoft.Extensions.AI")
            .GetResult();

        ArchAssert.Passes(result, "Use-case namespaces must not depend on Microsoft.Extensions.AI — wrap it behind a Minerva port.");
    }

    [Fact]
    public void Entities_DoNotDependOnAnyFrameworkPackage()
    {
        // why: Entities are the most reusable ring. No database, no HTTP, no DI,
        // no AI SDKs, no third-party frameworks should be reachable from here.
        var result = Types.InAssembly(Minerva)
            .That().ResideInNamespaceMatching(@"^Minerva\.(Models|Exceptions|Utilities)$")
            .Should().NotHaveDependencyOnAny(
                "Microsoft.Extensions.AI",
                "Microsoft.Extensions.DependencyInjection",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.Options",
                "Microsoft.Extensions.Logging",
                "Npgsql", "Pgvector", "OpenAI", "Polly", "Markdig")
            .GetResult();

        ArchAssert.Passes(result, "Minerva Entities (Models/Exceptions/Utilities) must not depend on any third-party framework.");
    }
}
