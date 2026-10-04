#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-all.sh <tag>"
    echo "Example: build-all.sh v0.1.0"
    exit 1
fi

TAG="$1"

cd "$(dirname "$0")"

./build-server.sh "$TAG"
./build-postgres.sh "$TAG"
./build-admin-ui.sh "$TAG"
./build-subject-ui.sh "$TAG"
./build-user-ui.sh "$TAG"

echo "Done."
