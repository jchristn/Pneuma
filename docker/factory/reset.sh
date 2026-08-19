#!/usr/bin/env bash
# ==========================================================================
# reset.sh - Reset the Pneuma docker environment to factory defaults (POSIX).
# Mirrors factory/reset.bat for macOS/Linux users.
# ==========================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCKER_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
FACTORY_DIR="$SCRIPT_DIR"
REPO_DIR="$(cd "$DOCKER_DIR/.." && pwd)"

echo
echo "=========================================================="
echo "  Pneuma - Reset to Factory Defaults"
echo "=========================================================="
echo
echo "WARNING: This is DESTRUCTIVE. It removes all Pneuma docker volumes"
echo "(Postgres, LiteGraph, Less3, DocumentAtom, Partio, RecallDB, Ollama,"
echo "Prometheus, Grafana, Tempo), clears logs/blobs/backups, and restores"
echo "factory config (including every subordinate service config). A fresh"
echo "'docker compose up -d' re-seeds the default administrator"
echo "(admin@pneuma / password)."
echo
read -r -p "Type 'RESET' to confirm: " CONFIRM
echo
if [ "$CONFIRM" != "RESET" ]; then
  echo "Aborted. No changes were made."
  exit 1
fi

echo "[1/4] Stopping containers and removing volumes..."
( cd "$DOCKER_DIR" && docker compose down -v || true )

echo "[2/4] Clearing runtime directories..."
rm -rf "$DOCKER_DIR/logs" "$DOCKER_DIR/blobs" "$DOCKER_DIR/backups"
mkdir -p "$DOCKER_DIR/logs/pneuma" "$DOCKER_DIR/blobs" "$DOCKER_DIR/backups"

echo "[3/4] Restoring factory configuration..."
cp -f "$FACTORY_DIR/pneuma.json" "$DOCKER_DIR/pneuma.json"
cp -f "$FACTORY_DIR/prometheus.yaml" "$DOCKER_DIR/prometheus.yaml"
cp -f "$FACTORY_DIR/tempo.yaml" "$DOCKER_DIR/tempo.yaml"
cp -f "$FACTORY_DIR/compose.yaml" "$DOCKER_DIR/compose.yaml"
mkdir -p "$DOCKER_DIR/less3"
cp -f "$FACTORY_DIR/less3/system.json" "$DOCKER_DIR/less3/system.json"
mkdir -p "$DOCKER_DIR/documentatom" "$DOCKER_DIR/litegraph" "$DOCKER_DIR/partio" "$DOCKER_DIR/recalldb"
cp -f "$FACTORY_DIR/documentatom/documentatom.json" "$DOCKER_DIR/documentatom/documentatom.json"
cp -f "$FACTORY_DIR/litegraph/litegraph.json" "$DOCKER_DIR/litegraph/litegraph.json"
cp -f "$FACTORY_DIR/partio/partio.json" "$DOCKER_DIR/partio/partio.json"
cp -f "$FACTORY_DIR/recalldb/recalldb.json" "$DOCKER_DIR/recalldb/recalldb.json"
mkdir -p "$DOCKER_DIR/postgres/init"
cp -f "$FACTORY_DIR/postgres/Dockerfile" "$DOCKER_DIR/postgres/Dockerfile"
cp -f "$FACTORY_DIR/postgres/init/01-create-databases.sql" "$DOCKER_DIR/postgres/init/01-create-databases.sql"
cp -f "$FACTORY_DIR/postgres/init/02-recalldb-extensions.sql" "$DOCKER_DIR/postgres/init/02-recalldb-extensions.sql"
mkdir -p "$DOCKER_DIR/grafana/provisioning/datasources" "$DOCKER_DIR/grafana/provisioning/dashboards"
cp -f "$FACTORY_DIR/grafana/provisioning/datasources/pneuma-datasources.yml" "$DOCKER_DIR/grafana/provisioning/datasources/pneuma-datasources.yml"
cp -f "$FACTORY_DIR/grafana/provisioning/dashboards/pneuma-dashboards.yml" "$DOCKER_DIR/grafana/provisioning/dashboards/pneuma-dashboards.yml"
mkdir -p "$REPO_DIR/assets/grafana"
cp -f "$FACTORY_DIR/assets/grafana/pneuma-observability-dashboard.json" "$REPO_DIR/assets/grafana/pneuma-observability-dashboard.json"

echo "[4/4] Factory reset complete."
echo
echo "To start the environment:"
echo "  cd $DOCKER_DIR && docker compose up -d"
echo
