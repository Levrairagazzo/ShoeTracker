#!/usr/bin/env bash
# Takes a one-off snapshot of the running app's SQLite database and writes it
# to ./backups as shoetracker-<timestamp>.db. Manual snapshots are never pruned
# by the automated backup service, so use this before risky operations (deploys
# with migrations, restores). Reuses the backup service's image and logic
# (backup/backup.sh), so it's safe to run while the app is up.
set -euo pipefail

cd "$(dirname "$0")/.."

docker compose run --rm --no-deps backup once
