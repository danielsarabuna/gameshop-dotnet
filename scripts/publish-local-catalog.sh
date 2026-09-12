#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
set -a
source "$repo_dir/.env.local"
set +a

: "${SUPABASE_URL:?SUPABASE_URL is required in .env.local}"
: "${SUPABASE_SERVICE_ROLE_KEY:?SUPABASE_SERVICE_ROLE_KEY is required in .env.local}"
bucket="${CATALOG_BUCKET:-dev}"

publish() {
  local source_file="$1"
  local object_path="$2"
  curl -fsS -X POST \
    "${SUPABASE_URL%/}/storage/v1/object/${bucket}/${object_path}" \
    -H "Authorization: Bearer ${SUPABASE_SERVICE_ROLE_KEY}" \
    -H "apikey: ${SUPABASE_SERVICE_ROLE_KEY}" \
    -H "Content-Type: application/json" \
    -H "x-upsert: true" \
    --data-binary "@$source_file" >/dev/null
  echo "Published ${bucket}/${object_path}"
}

publish "$repo_dir/config/catalog/dev/global/global/webshop_config_global.json" \
  "global/global/webshop_config_global.json"
publish "$repo_dir/config/catalog/dev/russia/ru_store/webshop_config_0.0.36.json" \
  "russia/ru_store/webshop_config_0.0.36.json"
publish "$repo_dir/config/catalog/dev/russia/ru_store/webshop_config_0.0.1.json" \
  "russia/ru_store/webshop_config_0.0.1.json"
