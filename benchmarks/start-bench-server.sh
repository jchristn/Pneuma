#!/usr/bin/env bash
# Run Pneuma from the working tree against the bench stack (benchmarks/docker/compose.yaml).
# REST + /metrics on http://127.0.0.1:28080. Logs, blobs, and the run log go to benchmarks/.run/.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$here/.."
mkdir -p "$here/.run"
dotnet build "$root/src/Pneuma.Server/Pneuma.Server.csproj" -c Release -f net10.0 >/dev/null
cd "$here/.run"
exec dotnet "$root/src/Pneuma.Server/bin/Release/net10.0/Pneuma.Server.dll" --config "$here/docker/pneuma.bench.json"
