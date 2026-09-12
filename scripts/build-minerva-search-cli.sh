#!/bin/bash
set -euo pipefail

PROJECT="src/Minerva.Search.Cli/Minerva.Search.Cli.csproj"
RUNTIME="osx-arm64"
CONFIG="Release"
PUBLISH_DIR="bin-minerva-search"

rm -rf "$PUBLISH_DIR"
dotnet restore "$PROJECT" -r "$RUNTIME"
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RUNTIME" --no-restore -o "$PUBLISH_DIR" \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
ls -lh "${PUBLISH_DIR}/minerva-search" "${PUBLISH_DIR}/appsettings.json"
