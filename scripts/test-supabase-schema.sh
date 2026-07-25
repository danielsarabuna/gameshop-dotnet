#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
container_name="webshop-supabase-schema-check-$$"

cleanup() {
  docker stop "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

fail_with_postgres_logs() {
  echo "PostgreSQL test container did not become stably ready." >&2
  docker logs "$container_name" >&2 || true
  exit 1
}

docker run --rm -d \
  --name "$container_name" \
  -e POSTGRES_PASSWORD=webshop-schema-check \
  -v "$repo_dir/supabase:/workspace/supabase:ro" \
  postgres:17-alpine >/dev/null

# A fresh official image briefly starts a temporary initialization server,
# stops it, and then launches the final server. Requiring consecutive healthy
# probes prevents CI from mistaking that temporary server for readiness.
ready_streak=0
for ((attempt = 1; attempt <= 60; attempt++)); do
  container_running="$(docker inspect --format '{{.State.Running}}' "$container_name" 2>/dev/null || true)"
  if [[ "$container_running" != "true" ]]; then
    fail_with_postgres_logs
  fi

  if docker exec "$container_name" pg_isready -U postgres >/dev/null 2>&1; then
    ready_streak=$((ready_streak + 1))
    if ((ready_streak >= 3)); then
      break
    fi
  else
    ready_streak=0
  fi
  sleep 1
done

if ((ready_streak < 3)); then
  fail_with_postgres_logs
fi

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
