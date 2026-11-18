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

if [ -f "$backup_file.sha256" ]; then
    expected=$(awk '{print $1}' "$backup_file.sha256")
    actual=$(sha256sum "$backup_file" | awk '{print $1}')
    if [ "$expected" != "$actual" ]; then
        echo "Checksum backup tidak cocok; restore dibatalkan." >&2
        exit 1
    fi
fi

db_name=$(psql "$DATABASE_URL" -Atc 'SELECT current_database()' 2>/dev/null || true)
case "$db_name" in
    *_restore|*_test) ;;
    *)
        if [ "${PARKIR_ALLOW_NON_TEST_RESTORE:-}" != "YES" ]; then
            echo "Target '$db_name' bukan database _restore/_test. Set PARKIR_ALLOW_NON_TEST_RESTORE=YES hanya untuk kasus darurat yang disetujui." >&2
            exit 1
        fi
        ;;
esac

pg_restore "$backup_file" --dbname "$DATABASE_URL" --clean --if-exists --no-owner
echo "Restore selesai ke database tujuan: $db_name"
