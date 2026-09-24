#!/bin/sh
# Snapshots the live SQLite database into /backups. Uses sqlite3's .backup
# (not a file copy), so it's safe while the API is writing. Each snapshot is
# integrity-checked before it's kept, and written under a temp name first so a
# half-written file never looks like a valid backup.
#
#   backup.sh        loop forever: snapshot every $BACKUP_INTERVAL_HOURS as
#                    shoetracker-auto-<timestamp>.db, keeping the newest $BACKUP_KEEP
#   backup.sh once   take one manual snapshot, shoetracker-<timestamp>.db, never pruned
set -eu

DB_PATH="${DB_PATH:-/data/shoetracker.db}"
BACKUP_DIR="${BACKUP_DIR:-/backups}"
BACKUP_INTERVAL_HOURS="${BACKUP_INTERVAL_HOURS:-24}"
BACKUP_KEEP="${BACKUP_KEEP:-14}"

log() {
  echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $*"
}

take_backup() {
  prefix="$1"

  # Read the database as the user that owns it (the API's non-root user). If
  # sqlite3 ran as root it could create root-owned -wal/-shm files in the
  # volume, after which the API could no longer write to its own database.
  owner=$(stat -c %u:%g "$DB_PATH")
  work=$(mktemp -d)
  chown "$owner" "$work"

  if ! su-exec "$owner" sqlite3 "$DB_PATH" ".backup '$work/snapshot.db'"; then
    log "ERROR: sqlite3 .backup failed."
    rm -rf "$work"
    return 1
  fi

  check=$(sqlite3 "$work/snapshot.db" "PRAGMA integrity_check;")
  if [ "$check" != "ok" ]; then
    log "ERROR: integrity check failed on new backup: $check"
    rm -rf "$work"
    return 1
  fi

  mkdir -p "$BACKUP_DIR"
  final="$BACKUP_DIR/$prefix-$(date -u +%Y%m%d-%H%M%S).db"
  mv "$work/snapshot.db" "$final.partial"
  mv "$final.partial" "$final"
  rm -rf "$work"
  log "Backup written to $final ($(wc -c < "$final") bytes)."
}

prune_automated() {
  # Timestamped names sort chronologically, so everything past the newest $BACKUP_KEEP goes.
  ls -1 "$BACKUP_DIR"/shoetracker-auto-*.db 2>/dev/null | sort -r | tail -n +"$((BACKUP_KEEP + 1))" | while read -r old; do
    rm -f "$old"
    log "Pruned $old"
  done
}

if [ "${1:-}" = "once" ]; then
  if [ ! -f "$DB_PATH" ]; then
    log "ERROR: no database at $DB_PATH to back up."
    exit 1
  fi
  take_backup shoetracker
  exit $?
fi

log "Backing up $DB_PATH every ${BACKUP_INTERVAL_HOURS}h, keeping the newest $BACKUP_KEEP automated snapshots."
while true; do
  # On a fresh deployment the API may not have created the database yet; check
  # again shortly rather than waiting a whole interval for the first backup.
  if [ ! -f "$DB_PATH" ]; then
    log "No database at $DB_PATH yet; retrying in 60s."
    sleep 60
    continue
  fi

  # A failed backup is logged but doesn't stop the loop; the next run retries.
  if take_backup shoetracker-auto; then
    prune_automated
  fi
  sleep "$((BACKUP_INTERVAL_HOURS * 3600))"
done
