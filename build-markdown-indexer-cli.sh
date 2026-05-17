#!/bin/bash
set -euo pipefail

PROJECT="src/Minerva.MarkdownIndexer/Minerva.MarkdownIndexer.csproj"
RUNTIME="osx-arm64"
CONFIG="Release"
PUBLISH_DIR="bin-markdown-indexer"

rm -rf "$PUBLISH_DIR"
dotnet restore "$PROJECT" -r "$RUNTIME"
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RUNTIME" --no-restore -o "$PUBLISH_DIR" \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
ls -lh "${PUBLISH_DIR}/markdown-indexer" "${PUBLISH_DIR}/appsettings.json"
