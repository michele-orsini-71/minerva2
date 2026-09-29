cd "$(dirname "$0")"
RUN=./minerva-markdown-indexer

# Frozen published binary (see scripts/build-minerva-markdown-indexer-cli.sh) so these runs
# are independent of ongoing source changes. Rebuild + recopy to pick up new code.
# Ordered cheapest-first so an early failure costs the least night time.
# All ingestions are incremental: rerunning skips already-completed documents.

# 1. Mini corpus — ~5 min
# DOTNET_ENVIRONMENT=wp111-nollm $RUN --config appsettings.json

# 2. Full corpus — ~2.5 h
# DOTNET_ENVIRONMENT=wp1283-nollm $RUN --config appsettings.json


