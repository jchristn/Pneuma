#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-postgres.sh <tag>"
    echo "Example: build-postgres.sh v0.1.0"
    exit 1
fi

TAG="$1"
IMAGE=jchristn77/pneuma-postgres

cd "$(dirname "$0")"

echo "Building $IMAGE:latest and $IMAGE:$TAG..."
docker buildx build \
    --builder cloud-jchristn77-jchristn77 \
    --platform linux/amd64,linux/arm64/v8 \
    -t "$IMAGE:latest" \
    -t "$IMAGE:$TAG" \
    -f docker/postgres/Dockerfile \
    --push \
    docker/postgres

echo "Done."
