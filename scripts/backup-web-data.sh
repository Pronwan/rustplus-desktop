#!/usr/bin/env bash
set -euo pipefail

# Create a consistent SQLite backup plus the key material needed to decrypt
# protected Rust+ credentials and to keep existing auth cookies valid.
output_dir="${1:-./backups}"
mkdir -p "$output_dir"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
archive="$output_dir/rustplus-web-$timestamp.tar.gz"

if ! docker compose ps --status running --services | grep -qx 'rustplus-web'; then
  echo "Error: rustplus-web service must be running for a consistent SQLite backup." >&2
  exit 1
fi

if ! docker compose exec -T rustplus-web sh -c 'command -v sqlite3 >/dev/null && test -f "$DATABASE_PATH"'; then
  echo "Error: sqlite3 or the configured database file is unavailable in the container." >&2
  exit 1
fi

tmp_archive="$(mktemp "$output_dir/.rustplus-web-backup.XXXXXX.tar.gz")"
trap 'rm -f "$tmp_archive"' EXIT
docker compose exec -T rustplus-web sh -c '
  set -eu
  backup="/tmp/rustplus-web-backup-$$.db"
  trap '"'"'rm -f "$backup"'"'"' EXIT
  sqlite3 "$DATABASE_PATH" ".backup $backup"
  integrity="$(sqlite3 "$backup" "PRAGMA integrity_check;")"
  if [ "$integrity" != "ok" ]; then
    echo "Error: SQLite integrity check failed: $integrity" >&2
    exit 1
  fi
  files="keys"
  [ ! -f /data/fcm-credentials.protected ] || files="$files fcm-credentials.protected"
  [ ! -f /data/fcm-persistent-ids.protected ] || files="$files fcm-persistent-ids.protected"
  tar -czf - -C /tmp "$(basename "$backup")" -C /data $files
' > "$tmp_archive"
if [ ! -s "$tmp_archive" ] || ! gzip -t "$tmp_archive" || ! tar -tzf "$tmp_archive" >/dev/null; then
  echo "Error: generated backup archive failed validation; original destination was not changed." >&2
  exit 1
fi
mv "$tmp_archive" "$archive"
trap - EXIT
echo "Backup created and verified: $archive"
echo "Treat this archive as sensitive: it contains credentials and data-protection keys."
