#!/bin/bash
set -euo pipefail

PROJECT="src/Minerva.Search.Bench/Minerva.Search.Bench.csproj"
RUNTIME="osx-arm64"
CONFIG="Release"
PUBLISH_DIR="bin-minerva-bench"

rm -rf "$PUBLISH_DIR"
dotnet restore "$PROJECT" -r "$RUNTIME"
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RUNTIME" --no-restore -o "$PUBLISH_DIR" \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
codesign --force -s - "${PUBLISH_DIR}/minerva-bench"
ls -lh "${PUBLISH_DIR}/minerva-bench" "${PUBLISH_DIR}/appsettings.json"
