#!/usr/bin/env bash
# Replaces the live SQLite database with a backup file, e.g.:
#
#   ./scripts/restore-db.sh backups/shoetracker-auto-20260925-030000.db
#
# Steps: integrity-check the backup, take a manual safety snapshot of the
# current database (so the restore itself can be undone), stop the API and
# backup services, swap the file into the shoe-data volume (dropping the old
# -wal/-shm files, which belong to the replaced database), merge the backed-up
# Data Protection key ring (backups/keys) into the volume so the restored
# database's encrypted Strava tokens can still be read, then start them again.
# Pass --yes to skip the confirmation prompt.
set -euo pipefail

cd "$(dirname "$0")/.."

usage() {
  echo "Usage: $0 [--yes] <backup-file>" >&2
  exit 1
}

ASSUME_YES=false
BACKUP_FILE=""
for arg in "$@"; do
  case "$arg" in
    --yes) ASSUME_YES=true ;;
    -*) usage ;;
    *) [ -z "$BACKUP_FILE" ] || usage; BACKUP_FILE="$arg" ;;
  esac
done
[ -n "$BACKUP_FILE" ] || usage

if [ ! -f "$BACKUP_FILE" ]; then
  echo "Backup file not found: $BACKUP_FILE" >&2
  exit 1
fi
BACKUP_ABS="$(cd "$(dirname "$BACKUP_FILE")" && pwd)/$(basename "$BACKUP_FILE")"

# Runs a shell command in the backup image with the live volume at /data and
# the chosen backup mounted read-only at /restore.db.
in_backup_container() {
  docker compose run --rm --no-deps -T --entrypoint sh -v "$BACKUP_ABS:/restore.db:ro" backup -c "$1" </dev/null
}

echo "Checking integrity of $BACKUP_FILE..."
# Copy first: the file is mounted read-only, and opening a WAL-mode database
# needs to create -wal/-shm files next to it.
CHECK=$(in_backup_container 'cp /restore.db /tmp/check.db && sqlite3 /tmp/check.db "PRAGMA integrity_check;" 2>&1') || true
if [ "$CHECK" != "ok" ]; then
  echo "Integrity check failed, not restoring: $CHECK" >&2
  exit 1
fi

if [ "$ASSUME_YES" != true ]; then
  read -r -p "This replaces the live database with $BACKUP_FILE. Type 'restore' to continue: " answer
  [ "$answer" = "restore" ] || { echo "Aborted."; exit 1; }
fi

if in_backup_container 'test -f /data/shoetracker.db'; then
  echo "Taking a safety snapshot of the current database..."
  ./scripts/backup-db.sh
fi

echo "Stopping api and backup services..."
docker compose stop api backup

echo "Restoring..."
# The file must end up owned by the API's non-root user, which owns /data.
in_backup_container '
  set -e
  owner=$(stat -c %u:%g /data)
  cp /restore.db /data/shoetracker.db.restoring
  chown "$owner" /data/shoetracker.db.restoring
  rm -f /data/shoetracker.db-wal /data/shoetracker.db-shm
  mv /data/shoetracker.db.restoring /data/shoetracker.db
  # Merge only (-n): the key ring is additive, and existing keys are still needed.
  if ls /backups/keys/*.xml >/dev/null 2>&1; then
    mkdir -p /data/keys
    cp -n /backups/keys/*.xml /data/keys/
    chown -R "$owner" /data/keys
  fi
'

echo "Starting api and backup services..."
docker compose up -d api backup

echo "Restored $BACKUP_FILE."
