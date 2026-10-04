#!/usr/bin/env bash
set -euo pipefail

if [ -z "${1:-}" ]; then
    echo "Usage: build-user-ui.sh <tag>"
    echo "Example: build-user-ui.sh v0.1.0"
    exit 1
fi

TAG="$1"
IMAGE=jchristn77/pneuma-user-ui

cd "$(dirname "$0")"

echo "Building $IMAGE:latest and $IMAGE:$TAG..."
docker buildx build \
    --builder cloud-jchristn77-jchristn77 \
    --platform linux/amd64,linux/arm64/v8 \
    -t "$IMAGE:latest" \
    -t "$IMAGE:$TAG" \
    -f user-dashboard/Dockerfile \
    --push \
    user-dashboard

echo "Done."
