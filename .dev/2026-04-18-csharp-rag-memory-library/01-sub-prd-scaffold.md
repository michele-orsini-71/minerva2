# Sub-PRD: Solution Scaffold & Foundation

**Parent**: [00-master-plan.md](./00-master-plan.md)
**Status**: Complete
**Dependency**: None
**Last Updated**: 2026-04-07

---

## Implementation Progress

| Step | Description | Status |
| ------ | ------------- | -------- |
| **1** | Create solution and projects via `dotnet new` | ✅ Complete |
| **2** | Add NuGet packages | ✅ Complete |
| **3** | Create exception hierarchy | ✅ Complete |
| **4** | Create model records | ✅ Complete |
| **5** | Create configuration types and credential resolver | ✅ Complete |
| **6** | Create HashHelper | ✅ Complete |
| **7** | Smoke test | ✅ Complete |

---

## Goal

Create a compilable solution with all projects, NuGet references, shared build properties, exception hierarchy, domain models, configuration types, and utility classes. This is the skeleton everything else imports.

---

## Implementation Steps

### Step 1: Create solution and projects via `dotnet new`

Use the dotnet CLI to scaffold all projects:

```bash
# Solution at repo root
dotnet new sln -n Minerva

# Core library
dotnet new classlib -n Minerva -o src/Minerva
dotnet sln add src/Minerva/Minerva.csproj

# Watcher client
dotnet new console -n Minerva.MarkdownWatcher -o src/Minerva.MarkdownWatcher
dotnet sln add src/Minerva.MarkdownWatcher/Minerva.MarkdownWatcher.csproj

# Unit tests
dotnet new xunit -n Minerva.Tests -o tests/Minerva.Tests
dotnet sln add tests/Minerva.Tests/Minerva.Tests.csproj

# Integration tests
dotnet new xunit -n Minerva.IntegrationTests -o tests/Minerva.IntegrationTests
dotnet sln add tests/Minerva.IntegrationTests/Minerva.IntegrationTests.csproj

# Project references
dotnet add src/Minerva.MarkdownWatcher reference src/Minerva/Minerva.csproj
dotnet add tests/Minerva.Tests reference src/Minerva/Minerva.csproj
dotnet add tests/Minerva.IntegrationTests reference src/Minerva/Minerva.csproj
```

Create `Directory.Build.props` at repo root for shared properties:
- `<Nullable>enable</Nullable>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`

Create `global.json` to pin the .NET SDK version (latest stable at implementation time).

Create `.gitignore` using `dotnet new gitignore`.

### Step 2: Add NuGet packages

**Core library** (`src/Minerva/`):
- `Npgsql` (v8+)
- `Pgvector` (v0.3+)
- `OpenAI` (v2.10+)
- `Polly` (v8+)
- `Microsoft.Extensions.AI`
- `Microsoft.SemanticKernel.Text`
- `Markdig`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Hosting.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

**Test projects**:
- `Moq` or `NSubstitute`
- `FluentAssertions` (optional)

Use `dotnet add package` for each.

### Step 3: Create exception hierarchy

**File**: `src/Minerva/Exceptions/MinervaException.cs`

Single file with all exception types:

```csharp
public class MinervaException : Exception { ... }
public class ConfigurationException : MinervaException { ... }
public class IngestionException : MinervaException { ... }
public class ChunkingException : IngestionException { ... }
public class EmbeddingException : IngestionException { ... }
public class StorageException : MinervaException { ... }
public class SearchException : MinervaException { ... }
public class ProviderUnavailableException : MinervaException { ... }
```

Each has constructors for `(string message)` and `(string message, Exception inner)`.

### Step 4: Create model records

All as C# `record` types (immutable, value equality).

**File**: `src/Minerva/Models/Document.cs`
```csharp
public record Document(
    string SourceId,
    string Title,
    string Text,
    Dictionary<string, object>? Metadata = null,
    Dictionary<string, AttachmentDescription>? Attachments = null);
```

**File**: `src/Minerva/Models/AttachmentDescription.cs`
```csharp
public record AttachmentDescription(
    string Description,
    string? SourcePath = null,
    Dictionary<string, object>? Metadata = null);
```

**File**: `src/Minerva/Models/Chunk.cs`
```csharp
public record Chunk(
    string Id,
    string SourceId,
    string CollectionName,
    int ChunkIndex,
    string Content,
    string ContentHash,
    string? ContextualPrefix = null,
    string? PrevChunkId = null,
    string? NextChunkId = null);
```

**File**: `src/Minerva/Models/Collection.cs`
```csharp
public record Collection(
    string Name,
    string? Description,
    string EmbeddingModel,
    int EmbeddingDimension,
    Dictionary<string, object>? Metadata = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastUpdatedAt = default);
```

**File**: `src/Minerva/Models/SearchResult.cs`
```csharp
public record SearchResult(
    string ChunkId,
    string SourceId,
    string CollectionName,
    string Content,
    double Score,
    Dictionary<string, object>? Metadata = null,
    string? ContextBefore = null,
    string? ContextAfter = null);
```

**File**: `src/Minerva/Models/SearchOptions.cs`
```csharp
public record SearchOptions(
    int TopK = 10,
    double HybridAlpha = 0.5,
    bool ExpandContext = false,
    Dictionary<string, object>? MetadataFilter = null);
```

**File**: `src/Minerva/Models/IngestionResult.cs`
```csharp
public record IngestionResult(
    int Added,
    int Updated,
    int Deleted,
    int Unchanged,
    TimeSpan Elapsed);
```

### Step 5: Create configuration types and credential resolver

**File**: `src/Minerva/Configuration/MinervaOptions.cs`
```csharp
public class MinervaOptions
{
    public string ConnectionString { get; set; }
    public ProviderOptions Embedding { get; set; }
    public ProviderOptions? Llm { get; set; }
    public ChunkingOptions Chunking { get; set; } = new();
}

public class ProviderOptions
{
    public string BaseUrl { get; set; }
    public string Model { get; set; }
    public string? ApiKey { get; set; }       // supports ${ENV_VAR} syntax
    public int? RequestsPerMinute { get; set; }
    public int Concurrency { get; set; } = 1;
    public int BatchSize { get; set; } = 1;
}

public class ChunkingOptions
{
    public int TargetChunkSize { get; set; } = 1200;
    public int ChunkOverlap { get; set; } = 200;
    public bool EnableSummarization { get; set; } = false;
    public bool EnableContextualization { get; set; } = false;
    public int LargeDocumentThreshold { get; set; } = 8000;
}
```

**File**: `src/Minerva/Configuration/CredentialResolver.cs`

Resolves `${ENV_VAR}` patterns in string values by reading from `Environment.GetEnvironmentVariable`. Refuses to pass through actual API key literals (regex guard for `sk-`, `AIza` patterns).

### Step 6: Create HashHelper

**File**: `src/Minerva/Utilities/HashHelper.cs`

```csharp
public static class HashHelper
{
    // SHA-256 of sourceId + chunkIndex, hex-encoded
    public static string GenerateChunkId(string sourceId, int chunkIndex) { ... }

    // SHA-256 of text content, hex-encoded
    public static string ComputeContentHash(string content) { ... }
}
```

### Step 7: Smoke test

**File**: `tests/Minerva.Tests/SmokeTests.cs`

A minimal test asserting the solution compiles and model records work:
- Create a `Document` record, verify properties
- Create an `IngestionResult` record, verify counts
- Assert `HashHelper.GenerateChunkId` is deterministic (same input → same output)
- Assert `HashHelper.ComputeContentHash` is deterministic

---

## Files Changed

### New Files

| File | Purpose |
| ------ | --------- |
| `Minerva.sln` | Solution file at repo root |
| `Directory.Build.props` | Shared MSBuild properties |
| `global.json` | Pin .NET SDK version |
| `.gitignore` | Standard .NET gitignore |
| `src/Minerva/Minerva.csproj` | Core library project |
| `src/Minerva.MarkdownWatcher/Minerva.MarkdownWatcher.csproj` | Watcher client project |
| `tests/Minerva.Tests/Minerva.Tests.csproj` | Unit test project |
| `tests/Minerva.IntegrationTests/Minerva.IntegrationTests.csproj` | Integration test project |
| `src/Minerva/Exceptions/MinervaException.cs` | Exception hierarchy |
| `src/Minerva/Models/Document.cs` | Document record |
| `src/Minerva/Models/AttachmentDescription.cs` | Attachment metadata record |
| `src/Minerva/Models/Chunk.cs` | Chunk record |
| `src/Minerva/Models/Collection.cs` | Collection record |
| `src/Minerva/Models/SearchResult.cs` | Search result record |
| `src/Minerva/Models/SearchOptions.cs` | Search options record |
| `src/Minerva/Models/IngestionResult.cs` | Ingestion result record |
| `src/Minerva/Configuration/MinervaOptions.cs` | Strongly-typed options |
| `src/Minerva/Configuration/CredentialResolver.cs` | `${ENV_VAR}` resolver |
| `src/Minerva/Utilities/HashHelper.cs` | Deterministic ID + hash generation |
| `tests/Minerva.Tests/SmokeTests.cs` | Compilation + model smoke tests |

---

## Verification Checklist

- [ ] `dotnet build Minerva.sln` compiles with zero errors and zero warnings
- [ ] `dotnet test tests/Minerva.Tests` passes all smoke tests
- [ ] `HashHelper.GenerateChunkId("doc1", 0)` returns same value on repeated calls
- [ ] `CredentialResolver` rejects literal API keys matching `sk-*` pattern

⏸️ **GATE**: Sub-PRD complete. Continue to next sub-PRD or `/dev-checkpoint`.
