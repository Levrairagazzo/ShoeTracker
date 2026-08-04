#!/usr/bin/env bash
# Takes a consistent snapshot of the running app's SQLite database and
# writes it to ./backups. Safe to run while the app is up: it uses sqlite3's
# .backup (not a raw file copy), which won't produce a corrupt copy of a
# file that's actively being written. The volume can't be mounted read-only
# here because WAL-mode SQLite needs to create/update its -wal/-shm sidecar
# files just to open the database for reading.
set -euo pipefail

cd "$(dirname "$0")/.."

VOLUME=$(docker volume ls --filter label=com.docker.compose.volume=shoe-data --format '{{.Name}}' | head -n1)

if [ -z "$VOLUME" ]; then
  echo "Could not find the shoe-data Docker volume. Is the app running via 'docker compose up'?" >&2
  exit 1
fi

mkdir -p backups
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
BACKUP_FILE="shoetracker-$TIMESTAMP.db"

docker run --rm \
  -v "$VOLUME:/data" \
  -v "$(pwd)/backups:/backup" \
  alpine:3.20 \
  sh -c "apk add --no-cache sqlite >/dev/null 2>&1 && sqlite3 /data/shoetracker.db '.backup /backup/$BACKUP_FILE'"

echo "Backup written to backups/$BACKUP_FILE"
