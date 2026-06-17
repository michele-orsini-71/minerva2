# Making `dotnet test` work for Integration Tests

## The honest answer

Integration tests need a real Postgres with pgvector — there's no way around that (it's what makes them integration tests, not unit tests). So `dotnet test` being "totally self-contained" isn't really achievable. But you can absolutely get rid of the env-var-on-the-command-line gymnastics.

## Recommended fix — pure config, no code changes

### 1. Add `tests/Minerva.IntegrationTests/.runsettings`

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <MINERVA_TEST_CONNSTRING>Host=localhost;Port=5432;Database=minerva_test;Username=michele</MINERVA_TEST_CONNSTRING>
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
```

### 2. Wire it up via `Directory.Build.props`

So `dotnet test` picks it up automatically, no `--settings` flag needed:

```xml
<PropertyGroup>
  <RunSettingsFilePath Condition="Exists('$(MSBuildProjectDirectory)\.runsettings')">$(MSBuildProjectDirectory)\.runsettings</RunSettingsFilePath>
</PropertyGroup>
```

Now `dotnet test` at the repo root works — integration tests pick up the connection string from `.runsettings` automatically.

## About the database itself

The tests still need the DB to exist with `pgvector` installed. You have two reasonable options:

- **Keep `scripts/run-integration-tests.sh`** but strip it down to just the DB bootstrap (drop the `-e` env flags since `.runsettings` handles them). Run it once after `brew services start postgresql@18`.
- **Use a docker-compose** so newcomers + CI do `docker compose up -d db` and then `dotnet test`. Same idea, more portable.

## For CI

The env-var pattern you dislike is actually the idiomatic CI approach: CI spins up a Postgres service container and injects `MINERVA_TEST_CONNSTRING`. A process-level env var overrides `.runsettings`, so the same `dotnet test` command works locally (uses `.runsettings`) and in CI (uses injected env var). No special CI path needed.

## One caveat

`.runsettings` will be committed with *your* local connection string (`Username=michele`). Two options:

1. Commit a `.runsettings.sample` and gitignore `.runsettings` — each dev copies + edits.
2. Commit `.runsettings` with the fixture's current default (`postgres/postgres`) and document that devs needing something else override via shell env var.

I'd pick option **2** — simpler, and the override path already exists.

---

## TL;DR — what to create

| File | Purpose |
| --- | --- |
| `tests/Minerva.IntegrationTests/.runsettings` | Sets `MINERVA_TEST_CONNSTRING` env var for the test process |
| `Directory.Build.props` (edit existing) | Adds `RunSettingsFilePath` so `dotnet test` auto-discovers the `.runsettings` |

After those two edits: `dotnet test` from the repo root runs everything, including integration tests, assuming the DB exists.
