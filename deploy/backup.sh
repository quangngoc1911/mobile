#!/usr/bin/env bash
set -Eeuo pipefail
: "${BACKUP_DIR:?Set BACKUP_DIR to an independent encrypted-storage staging directory}"
: "${FLOWERSHOP_DB_PASSWORD:?Set database password in the protected environment}"
: "${COMPOSE_PROJECT_NAME:?Set the exact Compose project name}"
# Chỉ sao lưu khi người vận hành đã dừng mọi ghi vào API và storage.
if [[ "${FLOWERSHOP_WRITES_QUIESCED:-}" != "yes" ]]; then
  echo "BLOCKED: set FLOWERSHOP_WRITES_QUIESCED=yes only after stopping all application writes." >&2
  exit 1
fi
storage_volume="${COMPOSE_PROJECT_NAME}_flowershop-storage"
# Docker có thể tự tạo volume mới khi sai tên; kiểm tra nhãn của đúng Compose project trước.
if ! docker volume inspect "$storage_volume" >/dev/null 2>&1; then
  echo "BLOCKED: storage volume does not exist: $storage_volume" >&2
  exit 1
fi
volume_project="$(docker volume inspect "$storage_volume" --format '{{ index .Labels "com.docker.compose.project" }}')"
volume_name="$(docker volume inspect "$storage_volume" --format '{{ index .Labels "com.docker.compose.volume" }}')"
if [[ "$volume_project" != "$COMPOSE_PROJECT_NAME" || "$volume_name" != "flowershop-storage" ]]; then
  echo "BLOCKED: storage volume labels do not match the requested Compose project." >&2
  exit 1
fi
stamp="$(date -u +%Y%m%d-%H%M%S)"
target="$BACKUP_DIR/$stamp"
mkdir -p "$target"
docker compose -p "$COMPOSE_PROJECT_NAME" -f deploy/docker-compose.vps.yml exec -T postgres pg_dump -U flowershop -d flowershop --format=custom > "$target/flowershop.dump"
docker run --rm --network none --mount "type=volume,src=$storage_volume,dst=/data,readonly" --mount "type=bind,src=$target,dst=/backup" alpine:3.20 tar -czf /backup/storage.tgz -C /data .
echo "Backup staged at $target; encrypt and copy it outside this VPS, then run restore verification."
