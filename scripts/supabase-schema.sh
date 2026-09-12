#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
action="${1:-plan}"
env_file="${2:-$repo_dir/.env.local}"

if [[ -f "$env_file" ]]; then
  set -a
  source "$env_file"
  set +a
fi

: "${SUPABASE_DB_URL:?Set SUPABASE_DB_URL in the selected env file or shell}"

case "$action" in
  plan)
    command -v supabase >/dev/null || { echo "Supabase CLI is required." >&2; exit 1; }
    supabase db push --db-url "$SUPABASE_DB_URL" --dry-run
    ;;
  apply)
    command -v supabase >/dev/null || { echo "Supabase CLI is required." >&2; exit 1; }
    if [[ "${SUPABASE_APPLY_CONFIRM:-}" != "YES" ]]; then
      echo "Refusing to mutate Supabase. Re-run with SUPABASE_APPLY_CONFIRM=YES after reviewing the plan." >&2
      exit 1
    fi
    supabase db push --db-url "$SUPABASE_DB_URL"
    ;;
  verify)
    command -v psql >/dev/null || { echo "psql is required." >&2; exit 1; }
    psql "$SUPABASE_DB_URL" -X -v ON_ERROR_STOP=1 \
      -f "$repo_dir/supabase/verify/webshop_schema.sql"
    ;;
  *)
    echo "Usage: $0 {plan|apply|verify} [env-file]" >&2
    exit 2
    ;;
esac
