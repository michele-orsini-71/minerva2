# Sub-PRD: Readiness Core

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Not Started
**Dependency**: None
**Last Updated**: 2026-04-25

---

## Implementation Progress

| Step | Description | Status |
|------|-------------|--------|
| **1** | Contracts and result types | ⬜ Not Started |
| **2** | `Redact` helper, `IReadinessProbeMarker`, `ReadinessChecker` impl | ⬜ Not Started |
| **3** | `ReadinessReportFormatter`, DI extension, safety-net warning | ⬜ Not Started |
| **4** | Architecture-test partition + unit tests | ⬜ Not Started |

---

## Goal

Establish the contracts and machinery for the readiness pipeline: interfaces, the sequential checker with per-check timeout, the probe marker, the redaction helper, the report formatter, and the DI extension. **No checks are added** in this sub-PRD; the library does not register itself for readiness yet. After this PRD, the surface exists and a future sub-PRD can plug checks in.

---

## Implementation Steps

### Step 1: Contracts and result types

**Files**:
- `src/Minerva/Readiness/ReadinessCategory.cs`
- `src/Minerva/Readiness/ReadinessCheckResult.cs`
- `src/Minerva/Readiness/ReadinessReport.cs`
- `src/Minerva/Readiness/IReadinessCheck.cs`
- `src/Minerva/Readiness/IReadinessChecker.cs`

Define the public surface from Decision 7 in the brief:

```csharp
public enum ReadinessCategory { Storage, Embedding, Llm, Client, Configuration }

public record ReadinessCheckResult(
    string Name,
    ReadinessCategory Category,
    bool Passed,
    string Code,            // UPPER_SNAKE segments separated by '.', e.g. "MINERVA.POSTGRES.CONNECTION_FAILED"
    string? Message,
    string? Remediation);

public record ReadinessReport(IReadOnlyList<ReadinessCheckResult> Results)
{
    public bool IsReady => Results.All(r => r.Passed);
}

public interface IReadinessCheck
{
    string Name { get; }
    ReadinessCategory Category { get; }
    Task<ReadinessCheckResult> RunAsync(CancellationToken ct);
}

public interface IReadinessChecker
{
    Task<ReadinessReport> CheckReadinessAsync(CancellationToken ct = default);
}
```

### Step 2: `Redact` helper, `IReadinessProbeMarker`, `ReadinessChecker` impl

**Files**:
- `src/Minerva/Readiness/Redact.cs` (internal static)
- `src/Minerva/Readiness/IReadinessProbeMarker.cs`
- `src/Minerva/Readiness/ReadinessProbeMarker.cs`
- `src/Minerva/Readiness/ReadinessChecker.cs`

`Redact.Apply(string?)` applies two compiled regexes:
- `(?i)Password\s*=\s*[^;]+` → `Password=***`
- `(?i)Bearer\s+\S+` → `Bearer ***`

Null-safe; returns `string.Empty` for null.

`IReadinessProbeMarker`:

```csharp
public interface IReadinessProbeMarker
{
    bool Probed { get; }
    void MarkProbed();
}
```

Implementation uses `volatile bool` or `Interlocked.Exchange`. Singleton lifetime. Idempotent `MarkProbed`.

**Per-check timeout shape**: an optional interface `IReadinessCheckTimeout { TimeSpan Timeout { get; } }`. The checker reads it as `is IReadinessCheckTimeout t ? t.Timeout : TimeSpan.FromSeconds(30)`. Decision is local to this PRD; consumed by checks in PRDs 04/05.

`ReadinessChecker`:

```csharp
public sealed class ReadinessChecker : IReadinessChecker
{
    private readonly IEnumerable<IReadinessCheck> _checks;
    private readonly IReadinessProbeMarker _marker;
    private readonly ILogger<ReadinessChecker> _logger;

    public async Task<ReadinessReport> CheckReadinessAsync(CancellationToken ct = default)
    {
        var results = new List<ReadinessCheckResult>();
        foreach (var check in _checks)
        {
            var timeout = (check as IReadinessCheckTimeout)?.Timeout ?? TimeSpan.FromSeconds(30);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            try
            {
                results.Add(await check.RunAsync(linked.Token));
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                results.Add(new ReadinessCheckResult(
                    check.Name, check.Category, false,
                    "MINERVA.READINESS.TIMEOUT",
                    $"Check '{check.Name}' did not complete within {timeout.TotalSeconds:0}s.",
                    "Investigate why this prerequisite is slow; if a local model is cold-loading, give it a warmup or extend the timeout."));
            }
            catch (Exception ex)
            {
                // Defensive: checks should not throw, but never propagate.
                results.Add(new ReadinessCheckResult(
                    check.Name, check.Category, false,
                    "MINERVA.READINESS.UNHANDLED",
                    Redact.Apply(ex.Message),
                    "Unexpected check failure — see logs for the full exception."));
                _logger.LogDebug(ex, "Readiness check '{Name}' threw unexpectedly", check.Name);
            }
        }
        _marker.MarkProbed();
        return new ReadinessReport(results);
    }
}
```

### Step 3: `ReadinessReportFormatter`, DI extension, safety-net warning

**Files**:
- `src/Minerva/Readiness/ReadinessReportFormatter.cs`
- `src/Minerva/Readiness/ReadinessServiceCollectionExtensions.cs`
- `src/Minerva/DI/MinervaStartupService.cs` (modify)

`ReadinessReportFormatter.LogReport(ILogger logger, ReadinessReport report)`:
- For each failing result: `logger.LogError("Readiness check '{Name}' failed [{Code}]: {Message}. Remediation: {Remediation}", ...)`
- One summary line: `logger.LogInformation("Readiness: {Passed}/{Total} checks passed", passed, total)`

`ReadinessServiceCollectionExtensions`:

```csharp
public static IServiceCollection AddMinervaReadinessCheck<T>(this IServiceCollection services)
    where T : class, IReadinessCheck
{
    AddMinervaReadinessCore(services);
    services.AddTransient<IReadinessCheck, T>();
    return services;
}

internal static IServiceCollection AddMinervaReadinessCore(this IServiceCollection services)
{
    services.TryAddSingleton<IReadinessProbeMarker, ReadinessProbeMarker>();
    services.TryAddSingleton<IReadinessChecker, ReadinessChecker>();
    return services;
}
```

`MinervaStartupService` modification — inject `IReadinessProbeMarker` via constructor as **optional** (use `IServiceProvider.GetService` resolution pattern, or a default null in the constructor signature so existing direct-construction call sites do not break). At the start of `StartAsync`:

```csharp
if (_marker is { Probed: false })
{
    _logger.LogWarning(
        "Minerva preflight was not invoked before host start; failures will surface as runtime exceptions.");
}
```

If `_marker` is null (readiness module not installed), no warning — full backward compatibility.

### Step 4: Architecture-test partition + unit tests

**Files**:
- `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` (modify)
- `tests/Minerva.UnitTests/Readiness/RedactTests.cs`
- `tests/Minerva.UnitTests/Readiness/ReadinessCheckerTests.cs`
- `tests/Minerva.UnitTests/Readiness/ReadinessReportFormatterTests.cs`
- `tests/Minerva.UnitTests/DI/MinervaStartupServiceWarningTests.cs`

Architecture-test update: add `Minerva.Readiness` to the use-case ring partition. Files in this PRD do not reference `Npgsql` or any provider — they belong in the use-case ring.

Unit-test coverage:
- **`RedactTests`**: `Password=secret;Host=…` → `Password=***;Host=…`; `Authorization: Bearer abc.def` → `Authorization: Bearer ***`; both at once; no-match → input unchanged; null → `string.Empty`.
- **`ReadinessCheckerTests`**: empty registry → `IsReady = true`; all-pass → `IsReady = true` with results in registration order; one-fail → `IsReady = false`; throwing check translated to failed result (not propagated); per-check timeout firing → `MINERVA.READINESS.TIMEOUT` result; marker is flipped after the run; idempotent `MarkProbed`.
- **`ReadinessReportFormatterTests`**: one `LogError` per failure with `{Name}/{Code}/{Message}/{Remediation}`; exactly one `LogInformation` summary.
- **`MinervaStartupServiceWarningTests`**: warn when marker `Probed = false`; no warn when `Probed = true`; no warn when marker is not registered (back-compat).

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `src/Minerva/Readiness/ReadinessCategory.cs` | Enum |
| `src/Minerva/Readiness/ReadinessCheckResult.cs` | Result record |
| `src/Minerva/Readiness/ReadinessReport.cs` | Report record |
| `src/Minerva/Readiness/IReadinessCheck.cs` | Check interface (+ optional `IReadinessCheckTimeout`) |
| `src/Minerva/Readiness/IReadinessChecker.cs` | Checker interface |
| `src/Minerva/Readiness/ReadinessChecker.cs` | Sequential implementation with per-check timeout |
| `src/Minerva/Readiness/IReadinessProbeMarker.cs` | Marker interface |
| `src/Minerva/Readiness/ReadinessProbeMarker.cs` | Marker impl |
| `src/Minerva/Readiness/Redact.cs` | Internal redaction helper |
| `src/Minerva/Readiness/ReadinessReportFormatter.cs` | Public log helper |
| `src/Minerva/Readiness/ReadinessServiceCollectionExtensions.cs` | DI extensions |
| `tests/Minerva.UnitTests/Readiness/RedactTests.cs` | Unit tests |
| `tests/Minerva.UnitTests/Readiness/ReadinessCheckerTests.cs` | Unit tests |
| `tests/Minerva.UnitTests/Readiness/ReadinessReportFormatterTests.cs` | Unit tests |
| `tests/Minerva.UnitTests/DI/MinervaStartupServiceWarningTests.cs` | Safety-net warning |

### Modified Files

| File | Changes |
|------|---------|
| `src/Minerva/DI/MinervaStartupService.cs` | Inject optional `IReadinessProbeMarker`; emit `LogWarning` in `StartAsync` if not probed |
| `tests/Minerva.ArchitectureTests/LayerDependencyTests.cs` | Add `Minerva.Readiness` to the use-case-ring partition |

---

## Verification Checklist

- [ ] `dotnet build src/Minerva/Minerva.csproj` succeeds with no new warnings
- [ ] `dotnet test tests/Minerva.UnitTests --filter Readiness` is green
- [ ] `dotnet test tests/Minerva.UnitTests --filter MinervaStartupServiceWarning` is green
- [ ] `dotnet test tests/Minerva.ArchitectureTests` is green
- [ ] No file in `src/Minerva/Readiness/` references `Npgsql` or any provider type
- [ ] `MarkProbed` is idempotent (asserted by test)

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
