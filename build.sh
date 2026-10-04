#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

echo "=== Backend build (Pneuma.sln) ==="
dotnet build src/Pneuma.sln -c Release

for d in admin-dashboard subject-dashboard user-dashboard; do
    echo "=== Dashboard build: $d ==="
    (cd "$d" && npm ci && npm run build)
done

echo "Build complete."
