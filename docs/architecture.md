# Minerva — Architecture

Minerva's code is organised after Robert C. Martin's **Clean Architecture**
model. This document describes the layers, the rule that governs them, and how
Minerva's namespaces map to the model. The mapping is enforced at build time by
the architecture tests in `tests/Minerva.ArchitectureTests/`.

## The layer model

Four concentric rings, from innermost to outermost:

| Ring | Role | Minerva namespaces |
| --- | --- | --- |
| **Entities** (Enterprise Business Rules) | Pure domain records, exceptions, and stateless helpers. The most reusable code. | `Minerva.Models`, `Minerva.Exceptions`, `Minerva.Utilities` |
| **Use Cases** (Application Business Rules) | Orchestrators. They coordinate entities to fulfil application operations. They talk to the outer world only through *ports* (interfaces they define). | `Minerva` (root — `MinervaEngine`, `IMinervaEngine`), `Minerva.Collections`, `Minerva.Ingestion`, `Minerva.Search` |
| **Interface Adapters** | Translate between the application and the outside world: repositories backed by a real database, providers backed by a real HTTP API, and so on. | `Minerva.Storage`, `Minerva.Providers` |
| **Frameworks & Drivers** | Integration with concrete hosts: configuration binding, DI container wiring, framework entry points. | `Minerva.Configuration`, `Minerva.DI` |

## The one rule

> **Source-code dependencies must point inward only.**

An outer ring may reference types in an inner ring; an inner ring must know
nothing about an outer one. When control flow has to move outward (a use case
needs to write to storage, a use case needs to call an LLM), the *Dependency
Inversion Principle* applies: the inner ring defines an interface (a *port*),
and the outer ring implements it (an *adapter*).

In concrete terms for Minerva:

- `Minerva.Models` does not know about any other Minerva namespace.
- `Minerva.Ingestion` may use `Minerva.Models` and ports defined within
  `Minerva.Ingestion`, but must not reference types in `Minerva.Storage`,
  `Minerva.Providers`, `Minerva.Configuration` or `Minerva.DI`.
- `Minerva.Storage` may reference `Minerva.Models` (to implement port interfaces
  that return domain records), but not `Minerva.Configuration`.
- External SDKs — `Microsoft.Extensions.AI`, `Npgsql`, `Pgvector`, `OpenAI`,
  `Polly` — belong to the outermost ring. They may be referenced only from the
  Adapter or Framework rings.

`Microsoft.Extensions.Logging` (`ILogger`) is treated as a permitted
cross-cutting abstraction: it is a minimal industry-standard interface, not a
framework dependency in the Uncle Bob sense.
`Microsoft.Extensions.DependencyInjection.Abstractions` is permitted only inside
`Minerva.DI`.

## Where ports live

Ports (interfaces that cross a boundary) should live *with the code that uses
them*, not with the code that implements them. If `Minerva.Collections` needs a
repository, the interface `ICollectionRepository` belongs inside
`Minerva.Collections` (or inside `Minerva.Models` if shared by several use
cases). `Minerva.Storage` provides the implementation and depends on the
interface — not the other way round.

## What the tests enforce

`Minerva.ArchitectureTests` contains one test per rule. Each test has a
`// why:` comment explaining what the rule protects against. The categories are:

1. **Inner rings depend on nothing outer.** One test per Entities namespace
   (`Models`, `Exceptions`, `Utilities`) and per Use-Case namespace
   (`Collections`, `Ingestion`, `Search`).
2. **Adapters do not depend on Framework.** `Providers` and `Storage` must not
   reference `Configuration` or `DI`.
3. **No third-party frameworks inside Entities.** No database drivers, AI SDKs,
   DI containers reachable from `Models`/`Exceptions`/`Utilities`.
4. **No `Microsoft.Extensions.AI` inside Use Cases.** `IChatClient` and
   `IEmbeddingGenerator` must be wrapped behind a Minerva port.
5. **Namespace coverage.** Every top-level `Minerva.*` namespace must be
   explicitly classified into a layer (see `NamespaceCoverageTests`).
   Introducing a new namespace forces the author to update the classification
   and add rules for it — this is the main guardrail against silent drift.

## When you add a new namespace

1. Decide which ring it belongs to. If it does not fit cleanly, that is a signal
   the ring model is wrong or the code is misplaced — discuss before continuing.
2. Add the namespace to `NamespaceCoverageTests.ClassifiedNamespaces`.
3. Add the corresponding dependency rule(s) to `LayerDependencyTests`.
4. Update the table above.

## Current state

All architecture tests currently pass. The code conforms to the ring model
described above. Past violations were resolved by:

- Moving shared data records (`ChunkWithEmbedding`, `ChunkRecord`,
  `ChunkSearchRecord`, `ChunkingOptions`, `ProviderOptions`) into
  `Minerva.Models`.
- Moving repository ports inward: `ICollectionRepository` /
  `ICollectionProvisioner` live in `Minerva.Collections`; `IChunkRepository` was
  split into `ISourceWriter` (in `Minerva.Ingestion`) and `IChunkQuery` (in
  `Minerva.Search`). `PostgresSourceRepository` implements both.
- Wrapping `Microsoft.Extensions.AI` behind Minerva-owned ports: `ILlmClient`
  and `IEmbeddingClient`, both defined in `Minerva.Ingestion` and implemented by
  the OpenAI-compatible adapters in `Minerva.Providers`.
