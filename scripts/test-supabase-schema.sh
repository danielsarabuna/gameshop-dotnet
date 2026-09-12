#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
container_name="webshop-supabase-schema-check-$$"

cleanup() {
  docker stop "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run --rm -d \
  --name "$container_name" \
  -e POSTGRES_PASSWORD=webshop-schema-check \
  -v "$repo_dir/supabase:/workspace/supabase:ro" \
  postgres:17-alpine >/dev/null

for _ in $(seq 1 30); do
  if docker exec "$container_name" pg_isready -U postgres >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

docker exec "$container_name" pg_isready -U postgres >/dev/null
docker exec "$container_name" psql -U postgres -v ON_ERROR_STOP=1 \
  -f /workspace/supabase/tests/bootstrap.sql >/dev/null

# A second pass proves that the baseline also converges an already initialized
# project instead of depending on one-time Dashboard state.
for _ in 1 2; do
  for migration in "$repo_dir"/supabase/migrations/*.sql; do
    docker exec "$container_name" psql -U postgres -v ON_ERROR_STOP=1 \
      -f "/workspace/supabase/migrations/$(basename "$migration")" >/dev/null
  done
done

docker exec "$container_name" psql -U postgres -v ON_ERROR_STOP=1 \
  -f /workspace/supabase/verify/webshop_schema.sql
docker exec "$container_name" psql -U postgres -v ON_ERROR_STOP=1 \
  -f /workspace/supabase/tests/webshop_flow.sql
