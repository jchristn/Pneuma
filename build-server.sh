#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-server.sh <tag>"
    echo "Example: build-server.sh v0.1.0"
    exit 1
fi

TAG="$1"
IMAGE=jchristn77/pneuma-server

cd "$(dirname "$0")"

echo "Building $IMAGE:latest and $IMAGE:$TAG..."
docker buildx build \
    --builder cloud-jchristn77-jchristn77 \
    --platform linux/amd64,linux/arm64/v8 \
    -t "$IMAGE:latest" \
    -t "$IMAGE:$TAG" \
    -f docker/server/Dockerfile \
    --push \
    .

echo "Done."
