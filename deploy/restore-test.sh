#!/usr/bin/env bash
set -Eeuo pipefail
: "${BACKUP_DIR:?Set BACKUP_DIR to the selected independent backup location}"
: "${BACKUP_STAMP:?Set BACKUP_STAMP to a backup directory to verify}"
target="$BACKUP_DIR/$BACKUP_STAMP"
test -s "$target/flowershop.dump"
test -s "$target/storage.tgz"
echo "PASS (file presence only): backup files exist and are non-empty."
echo "NOT VERIFIED: no database or image restore was performed. Restore into an isolated test environment and verify before production use."
