#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose=(docker compose --env-file "$repo_dir/.env.local" -f "$repo_dir/docker-compose.yml")

docker info >/dev/null
"${compose[@]}" config --quiet

for url in \
  http://localhost:5200/health \
  http://localhost:5100/health \
  http://localhost:5101/health \
  http://localhost:5103/health; do
  if ! curl -fsS --retry 12 --retry-delay 2 --retry-connrefused "$url" >/dev/null; then
    echo "FAILED: $url"
    "${compose[@]}" ps
    exit 1
  fi
  echo "OK: $url"
done

echo "WebShop is ready at http://localhost:5200"
