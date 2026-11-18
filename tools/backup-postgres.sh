#!/usr/bin/env bash
set -euo pipefail

# Backup PostgreSQL memakai connection URL tanpa mencetak kredensial.
# Penggunaan:
#   DATABASE_URL='postgresql://user:pass@host:5432/parkir' bash tools/backup-postgres.sh
#   BACKUP_DIR=/var/backups/parkir BACKUP_RETENTION_DAYS=30 ... bash tools/backup-postgres.sh

: "${DATABASE_URL:?Set DATABASE_URL (postgresql://...)}"
backup_dir="${BACKUP_DIR:-./backups}"
retention_days="${BACKUP_RETENTION_DAYS:-14}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
file="$backup_dir/parkir-$timestamp.dump"
umask 077

command -v pg_dump >/dev/null 2>&1 || {
    echo "pg_dump tidak ditemukan pada PATH." >&2
    exit 1
}

mkdir -p "$backup_dir"
pg_dump "$DATABASE_URL" --format=custom --no-owner --file "$file"
sha256sum "$file" > "$file.sha256"
echo "Backup dibuat: $file"
echo "Checksum: $file.sha256"

find "$backup_dir" -type f -name 'parkir-*.dump' -mtime "+$retention_days" -print -delete
find "$backup_dir" -type f -name 'parkir-*.dump.sha256' -mtime "+$retention_days" -print -delete
