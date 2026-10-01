# Minerva.ArchitectureTests

I decided to add these tests to keep an eye on architecture and non let
code dependencies flow in the wrong order.

I'm following Clean Architecture principles as described in
"Clean Architecture: A Craftsman's Guide to Software Structure and Design"
by Robert C. Martin (Uncle Bob); the single most important rule to
follow is this:

An outer ring may reference types in an inner ring; an inner ring must know
nothing about an outer one. When control flow has to move outward (a use case
needs to write to storage, a use case needs to call an LLM), the *Dependency
Inversion Principle* applies: the inner ring defines an interface (a *port*),
and the outer ring implements it (an *adapter*).

## The layer model

Four concentric rings, from innermost to outermost:

| Ring | Role | Minerva namespaces |
| --- | --- | --- |
| **Entities** (Enterprise Business Rules) | Pure domain records, exceptions, and stateless helpers. The most reusable code. | `Minerva.Models`, `Minerva.Exceptions`, `Minerva.Utilities` |
| **Use Cases** (Application Business Rules) | Orchestrators. They coordinate entities to fulfil application operations. They talk to the outer world only through *ports* (interfaces they define). | `Minerva` (root — `IIngestEngine`, `ISearchEngine`, builders), `Minerva.Collections`, `Minerva.Ingestion`, `Minerva.Search` |
| **Interface Adapters** | Translate between the application and the outside world: repositories backed by a real database, providers backed by a real HTTP API, and so on. | `Minerva.Storage`, `Minerva.Providers` |
| **Frameworks & Drivers** | Integration with concrete hosts: configuration binding. | `Minerva.Configuration` |

In concrete terms for Minerva:

- `Minerva.Models` does not know about any other Minerva namespace.
- `Minerva.Ingestion` may use `Minerva.Models` and ports defined within
  `Minerva.Ingestion`, but must not reference types in `Minerva.Storage`,
  `Minerva.Providers` or `Minerva.Configuration`.
- `Minerva.Storage` may reference `Minerva.Models` (to implement port interfaces
  that return domain records), but not `Minerva.Configuration`.
- External SDKs — `Microsoft.Extensions.AI`, `Npgsql`, `Pgvector`, `OpenAI`,
  `Polly` — belong to the outermost ring. They may be referenced only from the
  Adapter or Framework rings.

`Microsoft.Extensions.Logging` (`ILogger`) is treated as a permitted
cross-cutting abstraction: it is a minimal industry-standard interface, not a
framework dependency in the Uncle Bob sense.

## Running

```bash
dotnet test tests/Minerva.ArchitectureTests
```

Tests run as part of the normal test suite.

