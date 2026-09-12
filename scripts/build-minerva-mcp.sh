#!/bin/bash
set -euo pipefail

PROJECT="src/Minerva.Mcp/Minerva.Mcp.csproj"
RUNTIME="osx-arm64"
CONFIG="Release"
PUBLISH_DIR="bin-minerva-mcp"

rm -rf "$PUBLISH_DIR"
dotnet restore "$PROJECT" -r "$RUNTIME"
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RUNTIME" --no-restore -o "$PUBLISH_DIR" \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
ls -lh "${PUBLISH_DIR}/minerva-mcp" "${PUBLISH_DIR}/appsettings.json"
