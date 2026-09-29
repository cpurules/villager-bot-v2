#!/usr/bin/env bash
# Nightly backup of every bot database in the suite (docs/deployment.md §6).
#
#   deploy/backup.sh                 # dump each database in BOT_DATABASES to deploy/backups/
#
# Keeps the last $KEEP_DAYS days locally. If RCLONE_REMOTE is set (e.g. "b2:vh-bots-backups"), each new dump is also
# copied off the VM with rclone, and remote dumps older than $KEEP_DAYS days are pruned.
set -euo pipefail

cd "$(dirname "$0")"
KEEP_DAYS="${KEEP_DAYS:-14}"
BACKUP_DIR="${BACKUP_DIR:-$PWD/backups}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP_DIR"

databases="$(docker compose exec -T postgres printenv BOT_DATABASES)"
for db in $databases; do
  file="$BACKUP_DIR/${db}-${STAMP}.dump"
  # Custom format: compressed, and restorable table by table with pg_restore.
  docker compose exec -T postgres pg_dump -U postgres --format=custom "$db" > "$file"
  echo "backup: wrote $file ($(du -h "$file" | cut -f1))"

  if [[ -n "${RCLONE_REMOTE:-}" ]]; then
    rclone copy "$file" "$RCLONE_REMOTE/$db/"
  fi
done

find "$BACKUP_DIR" -name '*.dump' -mtime +"$KEEP_DAYS" -delete
if [[ -n "${RCLONE_REMOTE:-}" ]]; then
  rclone delete --min-age "${KEEP_DAYS}d" "$RCLONE_REMOTE"
fi
