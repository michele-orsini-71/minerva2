#!/usr/bin/env bash
# Runs every sweep in sequence, cheapest first, against the rerankers its config declares.
# Both reranker stages (BaseUrl + Model) come from src/Minerva.Search.Bench/appsettings.json
# plus the appsettings.<env>.json overlay, when DOTNET_ENVIRONMENT is set in the environment.
# The endpoints are read from that same config and each one must answer /health before a sweep
# starts, so a wrong env cannot silently produce results from the wrong model. The bench
# preflights every configured reranker even when a sweep does not use it, so the baseline
# sweep needs them up too.
#
# Usage: eval/experiments/run-all-sweeps.sh                     (any cwd; logs in eval/experiments/logs/)
#        DOTNET_ENVIRONMENT=qwen3-4b eval/experiments/run-all-sweeps.sh   (with a config overlay)
set -euo pipefail
cd "$(dirname "$0")/../.."

dotnet build -c Release src/Minerva.Search.Bench --nologo -v q
BENCH=src/Minerva.Search.Bench/bin/Release/net10.0/minerva-bench
CONFIG_DIR=src/Minerva.Search.Bench
LOGS=eval/experiments/logs
mkdir -p "$LOGS"

# sweep file                                      output dir
SWEEPS=(
  "eval/experiments/baseline/wp1283-nollm.toml  eval/experiments/baseline/runs"
  "eval/experiments/reranker/wp1283-nollm.toml  eval/experiments/reranker/runs"
)

# Prints one "<section> <host:port>" line per configured reranker stage, in config order.
reranker_endpoints() {
  local files=("$CONFIG_DIR/appsettings.json")
  [[ -n ${DOTNET_ENVIRONMENT:-} ]] && files+=("$CONFIG_DIR/appsettings.$DOTNET_ENVIRONMENT.json")
  python3 - "${files[@]}" <<'PY'
import json, sys
from urllib.parse import urlparse

def merge(base, overlay):
    for key, value in overlay.items():
        if isinstance(value, dict) and isinstance(base.get(key), dict):
            merge(base[key], value)
        else:
            base[key] = value

config = {}
for path in sys.argv[1:]:
    try:
        with open(path) as f:
            merge(config, json.load(f))
    except FileNotFoundError:
        pass

minerva = config.get("Minerva", {})
for section in ("Reranker", "CascadeReranker"):
    settings = minerva.get(section) or {}
    url = urlparse(settings.get("BaseUrl", ""))
    if url.hostname and url.port:
        print(f"{section} {url.hostname}:{url.port}")
PY
}

endpoints=$(reranker_endpoints)
echo "== config: ${DOTNET_ENVIRONMENT:-appsettings.json}  $(echo "$endpoints" | tr '\n' ' ')"

for row in "${SWEEPS[@]}"; do
  read -r sweep out <<<"$row"
  name=$(basename "$sweep" .toml)

  while read -r section address; do
    [[ -z $section ]] && continue
    curl -sf -m 5 "$address/health" >/dev/null \
      || { echo "$section at $address is down; stopping before $name"; exit 1; }
  done <<<"$endpoints"

  echo "== $name  $(date +%H:%M:%S)"
  "$BENCH" run --sweep "$sweep" --out "$out" --config "$CONFIG_DIR/appsettings.json" 2>&1 | tee "$LOGS/$name.log"
done

echo "== all sweeps done  $(date +%H:%M:%S)"
