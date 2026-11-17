#!/usr/bin/env bash
set -euo pipefail

# Pemindai secret sederhana untuk mencegah kredensial baru masuk ke repository.
# Hanya mencetak nama file/aturan, bukan nilai yang dicurigai.
cd "$(dirname "$0")/.."

status=0

check_pattern() {
    local rule="$1"
    local pattern="$2"
    local matches
    matches=$(git grep -l -E -e "$pattern" -- . ':!*.lock' || true)
    if [ -n "$matches" ]; then
        echo "SECRET-SCAN FAIL [$rule]"
        printf '%s\n' "$matches" | sed 's/^/  - /'
        status=1
    fi
}

check_pattern "private-key" '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
check_pattern "github-token" '(ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})'
check_pattern "aws-access-key" 'AKIA[0-9A-Z]{16}'
check_pattern "slack-token" 'xox[baprs]-[A-Za-z0-9-]{10,}'

tracked_env=$(git ls-files | grep -E '(^|/)\.env($|\.)' | grep -v '^\.env\.example$' || true)
if [ -n "$tracked_env" ]; then
    echo "SECRET-SCAN FAIL [tracked-env-file]"
    printf '%s\n' "$tracked_env" | sed 's/^/  - /'
    status=1
fi

tracked_keys=$(git ls-files | grep -E '\.(pem|key|p12|pfx|jks)$' || true)
if [ -n "$tracked_keys" ]; then
    echo "SECRET-SCAN FAIL [tracked-private-key-file]"
    printf '%s\n' "$tracked_keys" | sed 's/^/  - /'
    status=1
fi

if [ "$status" -eq 0 ]; then
    echo "SECRET-SCAN PASS: tidak ada pola secret berisiko tinggi pada tracked files."
fi

exit "$status"
