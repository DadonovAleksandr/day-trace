#!/bin/bash
set -euo pipefail

VERSION="${1:?Usage: deploy.sh <version>}"
DEPLOY_DIR="/opt/daytrace"
LOG_FILE="$DEPLOY_DIR/deploy.log"

log() { echo "[$(date -u '+%Y-%m-%d %H:%M:%S UTC')] $*" | tee -a "$LOG_FILE"; }

cd "$DEPLOY_DIR"

log "=== Deploying version $VERSION ==="
log "Pulling latest code..."
git fetch --all --tags
git checkout "v$VERSION"

log "Building containers with version $VERSION..."
APP_VERSION=$VERSION docker compose -f docker-compose.prod.yml build

log "Starting containers..."
APP_VERSION=$VERSION docker compose -f docker-compose.prod.yml up -d

log "Pruning old images..."
docker image prune -f

log "Checking health..."
sleep 10
if curl -sf http://localhost:8080/health | grep -q "healthy"; then
  log "Deploy v$VERSION successful"
else
  log "Health check failed!"
  exit 1
fi
