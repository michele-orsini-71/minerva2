# Minerva.ArchitectureTests

Build-time guardrails that enforce Minerva's Clean Architecture layering. The
layer model, the rule set, and the procedure for adding a new namespace are
documented in [`docs/architecture.md`](../../docs/architecture.md).

## Running

```bash
dotnet test tests/Minerva.ArchitectureTests
```

Tests run as part of the normal test suite. Some tests are expected to fail on
`main` — they encode rules the codebase does not yet satisfy and serve as a
backlog. See the "Current state" section of the architecture doc.

## Layout

- `LayerDependencyTests.cs` — one test per architectural rule; each has a
  `// why:` comment linking the rule to the Clean Architecture principle it
  enforces.
- `NamespaceCoverageTests.cs` — fails when a new top-level `Minerva.*` namespace
  appears without being classified into a layer. This is the main defence
  against silent drift.
- `ArchAssert.cs` — assertion helper that prints the failing types and points
  the reader at `docs/architecture.md`.

## When you add a new namespace or rule

Follow the procedure in
[`docs/architecture.md`](../../docs/architecture.md#when-you-add-a-new-namespace).
