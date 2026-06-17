# Architecture Tests — Project Brief

*Written retrospectively on 2026-04-21 after the session that introduced `tests/Minerva.ArchitectureTests/` and `docs/architecture.md`.*

## Context

The question that kicked off the work was: *how much does the `Minerva` base library conform to Uncle Bob's Clean Architecture model, and are the inward-only dependency arrows actually respected?*

Minerva is organised into eleven top-level namespaces (`Models`, `Exceptions`, `Utilities`, `Collections`, `Ingestion`, `Search`, `Storage`, `Providers`, `Configuration`, `DI`, plus the root `Minerva` namespace holding `MinervaEngine`). Mapping them onto the four concentric rings — Entities / Use Cases / Interface Adapters / Frameworks & Drivers — was straightforward; checking that the dependencies between them respected the rule was the real question.

## Options explored

Three families of approaches were considered, in increasing order of cost and payoff:

1. **Grep-based inspection.** Walk the folders, read `using` statements and representative files, flag suspicious edges. Fast, no tooling, but string-level — can't distinguish comment/string mentions from real type references, can't count references, can't resolve fully-qualified identifiers that don't appear in a `using`.
2. **Roslyn one-shot.** Load `Minerva.csproj` via `MSBuildWorkspace`, walk the `SemanticModel`, catalogue every resolved cross-namespace type reference. Throwaway console project in `/tmp`. Slower to set up (≈15 min with MSBuild package-version wrangling) but operates on real symbols at IL level.
3. **Permanent architecture tests.** Encode the rules as xUnit tests that run on every commit. Two libraries considered: *NetArchTest.Rules* (fluent, lightweight) and *ArchUnitNET* (richer, heavier). NetArchTest was chosen because the rules we needed — "types in namespace X must not depend on namespace Y" — are exactly its sweet spot.

A fourth option — a `dotnet script` / `.csx` file checked in under `scripts/` — was briefly considered as a middle ground between one-shot and test project, but rejected: it's re-runnable but not *self-executing*, and a script nobody remembers to run is equivalent to no script.

## grep vs. Roslyn, concretely

Both passes found the same five architectural fault lines — grep was directionally correct. What Roslyn added was:

- **Scale.** Grep flagged `Search → Storage` as "a few records leak"; Roslyn showed it was **32 references across 7 files**, with `ContextExpander.cs` alone responsible for 13 of them. Priority of the fix changed from "cleanup" to "structural".
- **Edges grep missed.** The edge `Providers → Configuration` (16 references) did not jump out of `using` statements — `ProviderFactory` uses `ProviderOptions` as a plain constructor parameter that grep's pattern-based scan didn't flag. The semantic model sees it instantly.
- **Property-level precision.** Roslyn pinpointed `IngestionPipeline.cs:84–94` building a `ChunkWithEmbedding` field by field, revealing that the use case is not just *referencing* a Storage DTO but actively *constructing* one — a stronger smell than grep's "uses type X" report.

For a one-shot, grep gets you to ≈85% of the answer in two minutes; Roslyn gets you to ≈95% in fifteen. On that axis alone the margin is real but modest.

## What actually mattered: repeatable checks

The more important realisation during the session was that **the analysis has diminishing value and the guardrail has compounding value.**

A one-off architectural audit — whether done by grep, Roslyn, or a human — describes the codebase on the day it was run. The next commit can silently undo every finding, and the next feature can introduce a new namespace the audit never mentioned. What keeps an architecture honest over time is not how accurately it was measured once, but whether the measurement *runs again on every commit*.

That reframed the choice. The question stopped being "grep or Roslyn?" and became "how do we install the rules as something that fails the build?". Once framed that way, the tooling followed: NetArchTest operates at the same IL level Roslyn does, so the precision advantage of Roslyn is preserved *and* made repeatable. The one-shot Roslyn pass retained value as a sanity check before writing the tests — confirming the edges, getting the failing-types list exactly right — but it was not the product.

## Solution adopted

A permanent test project, enforced at build time, plus two drift defences.

```text
tests/Minerva.ArchitectureTests/
├── Minerva.ArchitectureTests.csproj    # xUnit + NetArchTest.Rules + project ref to Minerva
├── LayerDependencyTests.cs              # one test per rule, each with a `// why:` comment
├── NamespaceCoverageTests.cs            # drift defence #1 (see below)
├── ArchAssert.cs                        # helper that prints failing types and points at the doc
└── README.md                            # orientation, links to docs/architecture.md
docs/
└── architecture.md                      # drift defence #2 (see below)
```

Eleven rule tests in total, covering:

- No inner ring depends on anything outer (one test per inner namespace).
- Adapters (`Providers`, `Storage`) do not depend on the Framework ring (`Configuration`, `DI`).
- Entities contain no third-party framework dependencies at all.
- Use-case namespaces do not depend on `Microsoft.Extensions.AI` — it must be wrapped behind a Minerva port.

Five of the eleven fail on `main` by design. They are kept red because they *are* the refactor backlog: they encode the rules the codebase should eventually satisfy. Marking them as skipped would convert a to-do list into a lie.

### Drift defence #1 — namespace coverage

`NamespaceCoverageTests.EveryTopLevelMinervaNamespace_IsClassifiedIntoALayer` enumerates every top-level `Minerva.*` namespace present in the compiled assembly and fails if any is not in the hand-curated `ClassifiedNamespaces` map. It also fails if an entry in the map no longer matches a live namespace. Adding a new namespace therefore *forces* the author to consciously assign it to a layer and add matching rules, rather than letting it drift in unclassified.

### Drift defence #2 — the architecture doc

`docs/architecture.md` describes the layer model, the single inward-dependency rule, the mapping from Minerva namespaces to rings, and — most importantly — a "When you add a new namespace" procedure. `ArchAssert` points at this document in every failure message, so a failing test carries its own explanation. Without this, a red architecture test is just a nuisance; with it, the test is a teaching moment.

## What there isn't yet

- **CI.** The project has no CI at present. In the meantime the tests rely on the local test run on every commit. When CI lands, these tests should run as part of the default PR check.
- **Coverage of the other source project.** Minerva.MarkdownWatcher has not been mapped onto the ring model; the tests only cover `Minerva`. If a similar analysis is wanted there, the test project can be extended to reference it.

## Key takeaway

One-shot audits — in any tool — have diminishing half-life. The value of this session is not in the violations it found; it is in turning those violations into failing tests and those tests into a document that a future reader, human or AI, will discover and understand. The chosen tools matter less than the fact that the check is now self-executing and self-explaining.
