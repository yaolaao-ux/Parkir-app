#!/usr/bin/env bash
set -euo pipefail

# Restore ke database terpisah. Jangan jalankan ke production tanpa persetujuan.
# Penggunaan:
#   RESTORE_CONFIRM=YES DATABASE_URL='postgresql://user:pass@host:5432/parkir_restore' \
#     bash tools/restore-postgres.sh backups/parkir-....dump

: "${DATABASE_URL:?Set DATABASE_URL tujuan restore}"
: "${1:?Berikan file .dump hasil backup}"
backup_file="$1"

if [ "${RESTORE_CONFIRM:-}" != "YES" ]; then
    echo "Set RESTORE_CONFIRM=YES untuk mengonfirmasi restore." >&2
    exit 1
fi

[ -f "$backup_file" ] || {
    echo "File backup tidak ditemukan: $backup_file" >&2
    exit 1
}
command -v pg_restore >/dev/null 2>&1 || {
    echo "pg_restore tidak ditemukan pada PATH." >&2
    exit 1
}

pg_restore "$backup_file" --dbname "$DATABASE_URL" --clean --if-exists --no-owner
echo "Restore selesai ke database tujuan."
